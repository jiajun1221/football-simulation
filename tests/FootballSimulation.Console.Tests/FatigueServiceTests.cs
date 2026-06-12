using FootballSimulation.Engine;
using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class FatigueServiceTests
{
    [Fact]
    public void RecoverTeamForNewMatch_RestoresAboutFiftyFiveStaminaPoints()
    {
        var team = CreateTeam();
        var player = team.Players[0];
        player.Stamina = 40;

        new FatigueService().RecoverTeamForNewMatch(team, recoveryPoints: 55);

        Assert.Equal(95, player.Stamina, precision: 4);
        Assert.Equal(player.Stamina, player.CurrentStamina, precision: 4);
    }

    [Fact]
    public void RecoverTeamAfterCompletedMatches_UsesAgeBasedCalendarGapRecovery()
    {
        var team = CreateTeam();
        var player = team.Players[0];
        player.Age = 27;
        player.Stamina = 62;
        var match = new Match
        {
            HomeTeam = team,
            AwayTeam = CreateTeam("Away"),
            CurrentMinute = 90,
            PlayerPerformances =
            [
                new PlayerMatchPerformance
                {
                    TeamName = team.Name,
                    PlayerName = player.Name,
                    WasSubstitute = false
                }
            ]
        };

        new FatigueService().RecoverTeamAfterCompletedMatches(team, calendarGap: 2, [match]);

        Assert.Equal(74, player.Stamina, precision: 4);
    }

    [Fact]
    public void RecoverTeamAfterCompletedMatches_RecoversUnusedSubstitutesMoreThanFullMatchStarters()
    {
        var team = CreateTeam();
        var starter = team.Players[0];
        var unusedSubstitute = team.Substitutes[0];
        starter.Age = 27;
        unusedSubstitute.Age = 27;
        starter.Stamina = 60;
        unusedSubstitute.Stamina = 60;
        var match = new Match
        {
            HomeTeam = team,
            AwayTeam = CreateTeam("Away"),
            CurrentMinute = 90,
            PlayerPerformances =
            [
                new PlayerMatchPerformance
                {
                    TeamName = team.Name,
                    PlayerName = starter.Name,
                    WasSubstitute = false
                }
            ]
        };

        new FatigueService().RecoverTeamAfterCompletedMatches(team, calendarGap: 2, [match]);

        Assert.True(unusedSubstitute.Stamina > starter.Stamina);
    }

    [Fact]
    public void RecoverTeamAfterCompletedMatches_FullMatchRestClearsHighSeasonFatigue()
    {
        var team = CreateTeam();
        var starter = team.Players[0];
        var restedPlayer = team.Substitutes[0];
        restedPlayer.Age = 27;
        restedPlayer.SeasonFatigue = 90;
        var match = new Match
        {
            HomeTeam = team,
            AwayTeam = CreateTeam("Away"),
            CurrentMinute = 90,
            PlayerPerformances =
            [
                new PlayerMatchPerformance
                {
                    TeamName = team.Name,
                    PlayerName = starter.Name,
                    WasSubstitute = false
                }
            ]
        };

        new FatigueService().RecoverTeamAfterCompletedMatches(team, calendarGap: 1, [match]);

        Assert.True(restedPlayer.SeasonFatigue < 60, $"Season fatigue was {restedPlayer.SeasonFatigue}.");
    }

    [Fact]
    public void CreateLiveMatch_StartsBothTeamsAndBenchesAtFullMatchStamina()
    {
        var team = CreateTeam("Home");
        var opponent = CreateTeam("Away");
        team.Players[0].Stamina = 42;
        team.Substitutes[0].Stamina = 55;
        opponent.Players[0].Stamina = 38;
        opponent.Substitutes[0].Stamina = 61;

        new MatchEngine().CreateLiveMatch(team, opponent);

        foreach (var player in team.Players.Concat(team.Substitutes).Concat(opponent.Players).Concat(opponent.Substitutes))
        {
            Assert.Equal(100, player.Stamina, precision: 4);
            Assert.Equal(100, player.CurrentStamina, precision: 4);
        }
    }

    [Fact]
    public void AdvanceMatch_DoesNotDrainUnusedSubstituteStamina()
    {
        var team = CreateTeam("Home");
        var opponent = CreateTeam("Away");
        var engine = new MatchEngine();
        var options = new MatchSimulationOptions { EnableAiSubstitutions = false, EnableDynamicFatigue = true };
        var match = engine.CreateLiveMatch(team, opponent, options);
        var unusedSubstitute = team.Substitutes[0];
        var staminaBefore = unusedSubstitute.Stamina;

        engine.AdvanceMatch(match, 1, 15, includeFulltime: false, options: options);

        Assert.Equal(staminaBefore, unusedSubstitute.Stamina, precision: 4);
    }

    [Fact]
    public void AdvanceMatch_OneMinuteTickDrainsStartingPlayerStamina()
    {
        var team = CreateTeam("Home");
        var opponent = CreateTeam("Away");
        var engine = new MatchEngine();
        var options = new MatchSimulationOptions { EnableAiSubstitutions = false, EnableDynamicFatigue = true };
        var match = engine.CreateLiveMatch(team, opponent, options);
        var starter = team.Players[1];
        var staminaBefore = starter.Stamina;

        engine.AdvanceMatch(match, 1, 1, includeFulltime: false, options: options);

        Assert.True(starter.Stamina < staminaBefore);
    }

    [Fact]
    public void AdvanceMatch_FirstHalfKeepsHalftimeStaminaReduced()
    {
        var team = CreateTeam("Home");
        var opponent = CreateTeam("Away");
        var engine = new MatchEngine();
        var options = new MatchSimulationOptions { EnableAiSubstitutions = false, EnableDynamicFatigue = true };
        var match = engine.CreateLiveMatch(team, opponent, options);
        var starter = team.Players[1];
        var staminaBefore = starter.Stamina;

        engine.AdvanceMatch(match, 1, 45, includeFulltime: false, options: options);

        Assert.Equal(MatchPhase.Halftime, match.CurrentPhase);
        Assert.True(starter.Stamina < staminaBefore);
    }

    [Fact]
    public void AdvanceMatch_DrainsSubstituteAfterHeEntersMatch()
    {
        var team = CreateTeam("Home");
        var opponent = CreateTeam("Away");
        var engine = new MatchEngine();
        var options = new MatchSimulationOptions { EnableAiSubstitutions = false, EnableDynamicFatigue = true };
        var match = engine.CreateLiveMatch(team, opponent, options);
        var starter = team.Players[1];
        var substitute = team.Substitutes[0];

        new SquadSelectionService().SwapStarterWithSubstitute(team, starter, substitute, match, substitutionMinute: 1);
        var staminaBefore = substitute.Stamina;

        engine.AdvanceMatch(match, 1, 15, includeFulltime: false, options: options);

        Assert.True(substitute.Stamina < staminaBefore);
    }

    [Fact]
    public void AdvanceMatch_HighIntensityAwayTeamDoesNotCollapseBySeventiethMinute()
    {
        var team = CreateTeam("Home");
        var opponent = CreateTeam("Away");
        team.Tactics.Tempo = 90;
        team.Tactics.PressingIntensity = 90;
        opponent.Tactics.Tempo = 90;
        opponent.Tactics.PressingIntensity = 90;
        var engine = new MatchEngine();
        var options = new MatchSimulationOptions
        {
            EnableAiSubstitutions = false,
            EnableDynamicFatigue = true,
            EnableInjuries = false
        };
        var match = engine.CreateLiveMatch(team, opponent, options);

        engine.AdvanceMatch(match, 1, 70, includeFulltime: false, options: options);

        var activeHomeAverage = team.Players.Where(player => player.IsOnPitch).Average(player => player.Stamina);
        var activeAwayAverage = opponent.Players.Where(player => player.IsOnPitch).Average(player => player.Stamina);
        Assert.True(activeAwayAverage >= 45, $"Away average stamina was {activeAwayAverage:0.0}%.");
        Assert.True(activeAwayAverage >= activeHomeAverage - 8, $"Away stamina {activeAwayAverage:0.0}% was too far below home {activeHomeAverage:0.0}%.");
    }

    [Fact]
    public void AdvanceMatch_GoalkeeperStaminaDrainsMuchSlowerThanOutfieldPlayers()
    {
        var team = CreateTeam("Home");
        var opponent = CreateTeam("Away");
        team.Tactics.Tempo = 90;
        team.Tactics.PressingIntensity = 90;
        opponent.Tactics.Tempo = 90;
        opponent.Tactics.PressingIntensity = 90;
        var engine = new MatchEngine();
        var options = new MatchSimulationOptions
        {
            EnableAiSubstitutions = false,
            EnableDynamicFatigue = true,
            EnableInjuries = false
        };
        var match = engine.CreateLiveMatch(team, opponent, options);

        engine.AdvanceMatch(match, 1, 70, includeFulltime: false, options: options);

        var goalkeeper = team.Players.Single(player => player.Position == Position.Goalkeeper);
        var outfieldAverage = team.Players
            .Where(player => player.Position != Position.Goalkeeper)
            .Average(player => player.Stamina);
        Assert.True(
            goalkeeper.Stamina >= outfieldAverage + 12,
            $"GK stamina {goalkeeper.Stamina:0.0}% should stay well above outfield average {outfieldAverage:0.0}%.");
    }

    [Fact]
    public void LowStaminaReducesPlayerEffectiveness()
    {
        var calculator = new TeamStrengthCalculator();
        var player = CreatePlayer("Forward", Position.Forward, isStarter: true);
        player.Attack = 85;
        player.Stamina = 100;
        var freshAttack = calculator.GetEffectiveAttack(player);

        player.Stamina = 25;
        var tiredAttack = calculator.GetEffectiveAttack(player);

        Assert.True(tiredAttack < freshAttack * 0.55);
    }

    private static Team CreateTeam(string name = "Test FC")
    {
        var starters = new List<Player>
        {
            CreatePlayer("Starter GK", Position.Goalkeeper, isStarter: true)
        };

        for (var index = 1; index < 11; index++)
        {
            starters.Add(CreatePlayer($"Starter {index}", Position.Midfielder, isStarter: true));
        }

        var substitutes = new List<Player>
        {
            CreatePlayer("Sub 1", Position.Forward, isStarter: false),
            CreatePlayer("Sub 2", Position.Defender, isStarter: false)
        };

        return new Team
        {
            Name = name,
            Formation = "4-3-3",
            Players = starters,
            Substitutes = substitutes,
            Tactics = new TeamTactics
            {
                Mentality = Mentality.Balanced,
                PressingIntensity = 75,
                Width = 50,
                Tempo = 75,
                DefensiveLine = 50
            }
        };
    }

    private static Player CreatePlayer(string name, Position position, bool isStarter)
    {
        return new Player
        {
            Name = name,
            Position = position,
            AssignedPosition = position.ToString(),
            PreferredPosition = position.ToString(),
            OverallRating = 75,
            Form = "Average",
            IsStarter = isStarter,
            CurrentForm = 50,
            Morale = 50,
            Attack = 75,
            Defense = 75,
            Passing = 75,
            Stamina = 80,
            CurrentStamina = 80,
            Finishing = 75
        };
    }
}
