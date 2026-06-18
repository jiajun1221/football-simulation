using FootballSimulation.Data;
using FootballSimulation.Engine;
using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class InjuryRiskServiceTests
{
    [Fact]
    public void MatchInjuryWeight_IncreasesForLowStaminaAndRecentLoad()
    {
        var service = new InjuryRiskService();
        var opponent = CreateTeam("Opponent", CreatePlayer("Opponent Defender", Position.Defender, 82));
        opponent.Tactics.PressingIntensity = 84;
        opponent.Players[0].Traits.Add(PlayerTrait.DivesIntoTackles);

        var freshPlayer = CreatePlayer("Fresh Player", Position.Midfielder, 92);
        var tiredPlayer = CreatePlayer("Tired Player", Position.Midfielder, 92);
        tiredPlayer.Stamina = 18;
        tiredPlayer.MatchesPlayedRecently = 5;
        tiredPlayer.Traits.Add(PlayerTrait.InjuryProne);

        var team = CreateTeam("Team", freshPlayer, tiredPlayer);

        var freshWeight = service.CalculatePlayerMatchInjuryWeight(freshPlayer, team, opponent, minute: 40, EventType.Attack);
        var tiredWeight = service.CalculatePlayerMatchInjuryWeight(tiredPlayer, team, opponent, minute: 82, EventType.Foul);

        Assert.True(tiredWeight > freshWeight * 3.0);
    }

    [Fact]
    public void MatchMinuteInjuryChance_IncreasesAfterPhysicalEventsLateInMatch()
    {
        var service = new InjuryRiskService();
        var homeTeam = CreateTeam("Home", CreatePlayer("Home Player", Position.Midfielder, 80));
        var awayTeam = CreateTeam("Away", CreatePlayer("Away Player", Position.Defender, 80));
        homeTeam.Players[0].Stamina = 24;
        homeTeam.Tactics.Tempo = 86;
        awayTeam.Tactics.PressingIntensity = 88;
        var match = new Match { HomeTeam = homeTeam, AwayTeam = awayTeam };

        var calmChance = service.CalculateMatchMinuteInjuryChance(match, minute: 25, EventType.Attack);
        var physicalChance = service.CalculateMatchMinuteInjuryChance(match, minute: 84, EventType.Foul);

        Assert.True(physicalChance > calmChance);
    }

    [Fact]
    public void PreparationInjuryChance_IncreasesForHighIntensityLoadedSquad()
    {
        var service = new InjuryRiskService();
        var lowRiskTeam = CreateSquad("Low Risk", recentLoad: 0, tempo: 45, pressing: 40);
        var highRiskTeam = CreateSquad("High Risk", recentLoad: 5, tempo: 88, pressing: 90);

        var lowRisk = service.CalculatePreparationInjuryChance(lowRiskTeam, roundNumber: 4);
        var highRisk = service.CalculatePreparationInjuryChance(highRiskTeam, roundNumber: 30);

        Assert.True(highRisk > lowRisk * 2.0);
    }

    [Fact]
    public void ApplyPostMatchLoad_IncreasesFullMatchLoadAndDecaysUnusedBench()
    {
        var service = new InjuryRiskService();
        var starter = CreatePlayer("Starter", Position.Midfielder, 82);
        var unusedBench = CreatePlayer("Unused Bench", Position.Forward, 76);
        unusedBench.MatchesPlayedRecently = 3;
        var homeTeam = new Team
        {
            Name = "Home",
            Players = [starter],
            Substitutes = [unusedBench]
        };
        var awayTeam = CreateTeam("Away", CreatePlayer("Away Player", Position.Defender, 80));
        var match = new Match
        {
            HomeTeam = homeTeam,
            AwayTeam = awayTeam,
            PlayerPerformances =
            [
                new PlayerMatchPerformance
                {
                    TeamName = homeTeam.Name,
                    PlayerName = starter.Name,
                    FatigueAtEnd = 70,
                    WasSubstitute = false
                }
            ]
        };

        service.ApplyPostMatchLoad(match);

        Assert.Equal(1, starter.MatchesPlayedRecently);
        Assert.Equal(2, unusedBench.MatchesPlayedRecently);
    }

    [Fact]
    public void ApplyPostMatchLoad_AddsSeasonFatigueFromMinutesAndTactics()
    {
        var service = new InjuryRiskService();
        var starter = CreatePlayer("Starter", Position.Midfielder, 82);
        starter.Age = 27;
        var homeTeam = new Team
        {
            Name = "Home",
            Players = [starter],
            Tactics = new TeamTactics
            {
                PressingIntensity = 85,
                Mentality = Mentality.Attacking
            }
        };
        var awayTeam = CreateTeam("Away", CreatePlayer("Away Player", Position.Defender, 80));
        var match = new Match
        {
            HomeTeam = homeTeam,
            AwayTeam = awayTeam,
            CurrentMinute = 90,
            PlayerPerformances =
            [
                new PlayerMatchPerformance
                {
                    TeamName = homeTeam.Name,
                    PlayerName = starter.Name,
                    WasSubstitute = false
                }
            ]
        };

        service.ApplyPostMatchLoad(match);

        Assert.Equal(8, starter.SeasonFatigue);
        Assert.Equal(1, starter.ConsecutiveStarts);
    }

    [Fact]
    public void ApplyPostMatchLoad_TreatsExtraTimeAsVeryHeavyFatigue()
    {
        var service = new InjuryRiskService();
        var starter = CreatePlayer("Starter", Position.Midfielder, 82);
        starter.Age = 27;
        var homeTeam = new Team
        {
            Name = "Home",
            Players = [starter],
            Tactics = new TeamTactics()
        };
        var awayTeam = CreateTeam("Away", CreatePlayer("Away Player", Position.Defender, 80));
        var match = new Match
        {
            HomeTeam = homeTeam,
            AwayTeam = awayTeam,
            CurrentMinute = 120,
            PlayerPerformances =
            [
                new PlayerMatchPerformance
                {
                    TeamName = homeTeam.Name,
                    PlayerName = starter.Name,
                    WasSubstitute = false
                }
            ]
        };

        service.ApplyPostMatchLoad(match);

        Assert.Equal(10, starter.SeasonFatigue);
        Assert.Equal(2, starter.MatchesPlayedRecently);
    }

    [Fact]
    public void FatigueInjuryRiskMultiplier_ScalesWithLowStaminaAndSeasonFatigue()
    {
        var freshPlayer = CreatePlayer("Fresh", Position.Midfielder, 82);
        freshPlayer.Stamina = 90;
        freshPlayer.SeasonFatigue = 10;
        var tiredPlayer = CreatePlayer("Tired", Position.Midfielder, 82);
        tiredPlayer.Stamina = 35;
        tiredPlayer.SeasonFatigue = 85;

        var freshRisk = InjuryRiskService.GetFatigueInjuryRiskMultiplier(freshPlayer);
        var tiredRisk = InjuryRiskService.GetFatigueInjuryRiskMultiplier(tiredPlayer);

        Assert.True(tiredRisk > freshRisk * 4);
    }

    [Fact]
    public void ApplyInjury_SetsRecoveryMetadataAndRemovesMatchStamina()
    {
        var player = CreatePlayer("Injured Player", Position.Forward, 84);

        InjuryRiskService.ApplyInjury(player, "sprint muscle pull", new Random(2));

        Assert.True(player.IsInjured);
        Assert.True(player.NewlyInjuredThisMatch);
        Assert.False(string.IsNullOrWhiteSpace(player.InjuryType));
        Assert.NotNull(player.InjurySeverity);
        Assert.True(player.InjuryRecoveryMatches > 0);
        Assert.Equal(0, player.Stamina);
    }

    [Fact]
    public void SimulateMatch_CanProduceInjuriesAcrossHighLoadPhysicalMatches()
    {
        var seedDataService = new SeedDataService();
        var engine = new MatchEngine();
        var injuryCount = 0;

        for (var seed = 1; seed <= 24; seed++)
        {
            var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
            ConfigureHighInjuryRiskTeam(homeTeam);
            ConfigureHighInjuryRiskTeam(awayTeam);

            var result = engine.SimulateMatch(homeTeam, awayTeam, seed: seed);
            injuryCount += result.Events.Count(matchEvent => matchEvent.EventType == EventType.Injury);
        }

        Assert.True(injuryCount > 0);
    }

    private static Team CreateSquad(string name, int recentLoad, int tempo, int pressing)
    {
        var players = Enumerable.Range(1, 16)
            .Select(index =>
            {
                var player = CreatePlayer($"{name} Player {index}", index == 1 ? Position.Goalkeeper : Position.Midfielder, 78);
                player.MatchesPlayedRecently = recentLoad;
                if (index <= 4)
                {
                    player.Role = PlayerRole.Starter;
                }

                return player;
            })
            .ToList();

        return new Team
        {
            Name = name,
            Players = players.Take(11).ToList(),
            Substitutes = players.Skip(11).ToList(),
            Tactics = new TeamTactics
            {
                Tempo = tempo,
                PressingIntensity = pressing
            }
        };
    }

    private static Team CreateTeam(string name, params Player[] players)
    {
        return new Team
        {
            Name = name,
            Players = players.ToList(),
            Tactics = new TeamTactics()
        };
    }

    private static Player CreatePlayer(string name, Position position, int physical)
    {
        return new Player
        {
            Name = name,
            Position = position,
            PreferredPosition = position == Position.Goalkeeper ? "GK" : "CM",
            OverallRating = 78,
            Physical = physical,
            Stamina = 88,
            CurrentStamina = 88,
            IsOnPitch = true,
            Role = PlayerRole.Rotation
        };
    }

    private static void ConfigureHighInjuryRiskTeam(Team team)
    {
        team.Tactics.Tempo = 95;
        team.Tactics.PressingIntensity = 95;

        foreach (var player in team.Players.Concat(team.Substitutes))
        {
            player.MatchesPlayedRecently = 5;
        }

        foreach (var defender in team.Players.Where(player => player.Position == Position.Defender).Take(2))
        {
            if (!defender.Traits.Contains(PlayerTrait.DivesIntoTackles))
            {
                defender.Traits.Add(PlayerTrait.DivesIntoTackles);
            }
        }
    }
}
