using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class SquadSelectionServiceTests
{
    [Fact]
    public void SwapStarterWithSubstitute_PreservesElevenStarters()
    {
        var service = new SquadSelectionService();
        var team = CreateTeam();
        var starter = team.Players[1];
        var substitute = team.Substitutes[1];

        var result = service.SwapStarterWithSubstitute(team, starter, substitute);

        Assert.True(result.Success);
        Assert.Equal(11, team.Players.Count);
        Assert.Contains(substitute, team.Players);
        Assert.Contains(starter, team.Substitutes);
        Assert.True(substitute.IsStarter);
        Assert.False(starter.IsStarter);
        Assert.True(substitute.IsOnPitch);
        Assert.False(starter.IsOnPitch);
    }

    [Fact]
    public void SwapStarterWithSubstitute_RecordsHalftimeSubstitution()
    {
        var service = new SquadSelectionService();
        var team = CreateTeam();
        var match = new Match { HomeTeam = team, AwayTeam = CreateTeam("Opponent") };
        var starter = team.Players[1];
        var substitute = team.Substitutes[1];

        var result = service.SwapStarterWithSubstitute(team, starter, substitute, match, 45);

        Assert.True(result.Success);
        var substitution = Assert.Single(match.Substitutions);
        Assert.Equal(45, substitution.Minute);
        Assert.Equal(starter.Name, substitution.PlayerOffName);
        Assert.Equal(substitute.Name, substitution.PlayerOnName);
    }

    [Fact]
    public void SwapStarterWithSubstitute_RejectsMoreThanFiveMatchSubstitutions()
    {
        var service = new SquadSelectionService();
        var team = CreateTeam(substituteCount: 7);
        var match = new Match { HomeTeam = team, AwayTeam = CreateTeam("Opponent") };

        for (var index = 0; index < 5; index++)
        {
            var result = service.SwapStarterWithSubstitute(team, team.Players[index + 1], team.Substitutes[0], match, 45);
            Assert.True(result.Success);
        }

        var rejected = service.SwapStarterWithSubstitute(team, team.Players[6], team.Substitutes[0], match, 45);

        Assert.False(rejected.Success);
        Assert.Equal(5, service.CountTeamSubstitutions(match, team.Name));
    }

    [Fact]
    public void SwapStarterWithSubstitute_RejectsPlayerAlreadySubstitutedOff()
    {
        var service = new SquadSelectionService();
        var team = CreateTeam();
        var match = new Match { HomeTeam = team, AwayTeam = CreateTeam("Opponent") };
        var originalStarter = team.Players[1];
        var firstSubstitute = team.Substitutes[1];

        var firstSwap = service.SwapStarterWithSubstitute(team, originalStarter, firstSubstitute, match, 60);
        Assert.True(firstSwap.Success);
        Assert.Contains(originalStarter, team.Substitutes);

        var rejectedReturn = service.SwapStarterWithSubstitute(team, team.Players[2], originalStarter, match, 70);

        Assert.False(rejectedReturn.Success);
        Assert.Contains("cannot return", rejectedReturn.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(match.Substitutions);
        Assert.Contains(originalStarter, team.Substitutes);
        Assert.DoesNotContain(originalStarter, team.Players);
    }

    [Fact]
    public void AiManager_DoesNotBringBackPlayerAlreadySubstitutedOff()
    {
        var team = CreateTeam(substituteCount: 3);
        var match = new Match { HomeTeam = team, AwayTeam = CreateTeam("Opponent") };
        var tiredStarter = team.Players[1];
        var subbedOffPlayer = team.Substitutes[1];
        var eligibleSubstitute = team.Substitutes[2];
        PositionSuitabilityService.EnsurePositionMetadata(subbedOffPlayer, "CB");
        PositionSuitabilityService.EnsurePositionMetadata(eligibleSubstitute, "CB");
        tiredStarter.Stamina = 4;
        subbedOffPlayer.OverallRating = 99;
        eligibleSubstitute.OverallRating = 60;
        match.Substitutions.Add(new MatchSubstitution
        {
            Minute = 50,
            TeamName = team.Name,
            PlayerOffName = subbedOffPlayer.Name,
            PlayerOnName = "Earlier Substitute"
        });

        var decision = new AiManagerService().TryMakeSubstitution(
            match,
            team,
            minute: 60,
            new MatchSimulationOptions(),
            new Random(1));

        Assert.NotNull(decision);
        Assert.Equal(eligibleSubstitute.Name, decision.PlayerOn.Name);
        Assert.NotEqual(subbedOffPlayer.Name, decision.PlayerOn.Name);
    }

    [Fact]
    public void SubstituteKeepsFresherStaminaThanTiredStarter()
    {
        var team = CreateTeam();
        var starter = team.Players[1];
        var substitute = team.Substitutes[1];
        starter.CurrentStamina = 35;
        substitute.CurrentStamina = substitute.Stamina;

        new SquadSelectionService().SwapStarterWithSubstitute(team, starter, substitute);

        Assert.True(substitute.CurrentStamina > starter.CurrentStamina);
    }

    [Fact]
    public void SwapStarterWithSubstitute_RejectsSentOffStarter()
    {
        var service = new SquadSelectionService();
        var team = CreateTeam();
        var starter = team.Players[1];
        var substitute = team.Substitutes[1];
        starter.IsSentOff = true;
        starter.IsOnPitch = false;

        var result = service.SwapStarterWithSubstitute(team, starter, substitute);

        Assert.False(result.Success);
        Assert.Contains(starter, team.Players);
        Assert.Contains(substitute, team.Substitutes);
        Assert.False(substitute.IsStarter);
        Assert.False(substitute.IsOnPitch);
    }

    [Fact]
    public void SwapStarterWithSubstitute_RejectsOutfieldReplacementForGoalkeeper()
    {
        var service = new SquadSelectionService();
        var team = CreateTeam();
        var goalkeeper = team.Players[0];
        var outfieldSubstitute = team.Substitutes[1];

        var result = service.SwapStarterWithSubstitute(team, goalkeeper, outfieldSubstitute);

        Assert.False(result.Success);
        Assert.Contains(goalkeeper, team.Players);
        Assert.Contains(outfieldSubstitute, team.Substitutes);
    }

    [Fact]
    public void RepairGoalkeeperSlot_PromotesBackupGoalkeeperOnly()
    {
        var team = CreateTeam();
        var originalGoalkeeper = team.Players[0];
        var backupGoalkeeper = team.Substitutes[0];
        team.Players.Remove(originalGoalkeeper);
        team.Substitutes.Add(originalGoalkeeper);

        var jordanHenderson = CreatePlayer("Jordan Henderson", Position.Midfielder, isStarter: true);
        jordanHenderson.PreferredPosition = "CM";
        team.Players.Insert(0, jordanHenderson);

        var result = LineupValidationService.RepairGoalkeeperSlot(team);

        Assert.True(result.IsValid);
        Assert.True(result.WasRepaired);
        Assert.Contains(backupGoalkeeper, team.Players);
        Assert.DoesNotContain(jordanHenderson, team.Players.Where(PositionSuitabilityService.IsGoalkeeperCapable));
        Assert.False(PositionSuitabilityService.IsGoalkeeperCapable(jordanHenderson));
    }

    [Fact]
    public void RepairUnavailablePlayers_RemovesSuspendedStarterFromLineup()
    {
        var team = CreateTeam();
        var suspendedStarter = team.Players[1];
        var replacement = team.Substitutes[1];
        suspendedStarter.SuspendedMatches = 1;

        var result = LineupValidationService.RepairUnavailablePlayers(team);

        Assert.True(result.IsValid);
        Assert.DoesNotContain(suspendedStarter, team.Players);
        Assert.Contains(suspendedStarter, team.Substitutes);
        Assert.Contains(replacement, team.Players);
        Assert.False(suspendedStarter.IsStarter);
        Assert.False(suspendedStarter.IsOnPitch);
    }

    [Fact]
    public void AiLineupSelection_UsesNaturalCenterBacksOverHighRatedAttackers()
    {
        var team = new Team
        {
            Name = "Manchester Test",
            Formation = "4-3-3",
            Players =
            [
                CreateExactPlayer("Starter GK", Position.Goalkeeper, "GK", 82, true),
                CreateExactPlayer("High Rated Winger", Position.Forward, "LW", 92, true),
                CreateExactPlayer("Creative Midfielder", Position.Midfielder, "CAM", 91, true),
                CreateExactPlayer("Natural Center Back A", Position.Defender, "CB", 82, true),
                CreateExactPlayer("Natural Right Back", Position.Defender, "RB", 80, true),
                CreateExactPlayer("Central Midfielder A", Position.Midfielder, "CM", 84, true),
                CreateExactPlayer("Central Midfielder B", Position.Midfielder, "CM", 83, true),
                CreateExactPlayer("Attacking Midfielder", Position.Midfielder, "CAM", 86, true),
                CreateExactPlayer("Left Winger", Position.Forward, "LW", 85, true),
                CreateExactPlayer("Striker", Position.Forward, "ST", 88, true),
                CreateExactPlayer("Right Winger", Position.Forward, "RW", 85, true)
            ],
            Substitutes =
            [
                CreateExactPlayer("Natural Center Back B", Position.Defender, "CB", 79, false),
                CreateExactPlayer("Natural Left Back", Position.Defender, "LB", 78, false),
                CreateExactPlayer("Defensive Midfielder", Position.Midfielder, "CDM", 80, false),
                CreateExactPlayer("Sub GK", Position.Goalkeeper, "GK", 72, false)
            ]
        };

        AiLineupSelectionService.BuildRealisticLineup(team);

        var slots = FormationSlotService.GetSlots(team.Formation);
        var centerBacks = team.Players
            .Select((player, index) => new { Player = player, Slot = slots[index] })
            .Where(item => item.Slot == "CB")
            .Select(item => item.Player)
            .ToList();

        Assert.All(centerBacks, player => Assert.True(
            PositionCompatibilityService.GetCompatibilityScore(player, "CB") > PositionCompatibilityService.Emergency,
            $"{player.Name} should not be used at CB."));
        Assert.DoesNotContain(centerBacks, player => player.PreferredPosition is "LW" or "RW" or "ST" or "CAM");
    }

    [Fact]
    public void AiManager_ChoosesDefensiveReplacementForCenterBack()
    {
        var service = new AiManagerService();
        var team = CreateTeam();
        var centerBack = team.Players[1];
        PositionSuitabilityService.EnsurePositionMetadata(centerBack, "CB");
        centerBack.IsInjured = true;
        team.Substitutes =
        [
            CreateExactPlayer("Attacking Star", Position.Forward, "LW", 92, false),
            CreateExactPlayer("Backup Center Back", Position.Defender, "CB", 76, false),
            CreateExactPlayer("Backup Midfielder", Position.Midfielder, "CM", 82, false),
            CreateExactPlayer("Sub GK", Position.Goalkeeper, "GK", 72, false)
        ];
        var opponent = CreateTeam("Opponent");
        var match = new Match { HomeTeam = team, AwayTeam = opponent };

        var decision = service.TryMakeSubstitution(
            match,
            team,
            minute: 60,
            new MatchSimulationOptions { EnableAiSubstitutions = true },
            new Random(1));

        Assert.NotNull(decision);
        Assert.Equal(centerBack.Name, decision!.PlayerOff.Name);
        Assert.Equal("Backup Center Back", decision.PlayerOn.Name);
    }

    private static Team CreateTeam(string name = "Test FC", int substituteCount = 7)
    {
        var players = new List<Player>
        {
            CreatePlayer("Starter GK", Position.Goalkeeper, isStarter: true)
        };

        for (var index = 1; index < 11; index++)
        {
            players.Add(CreatePlayer($"Starter {index}", Position.Defender, isStarter: true));
        }

        var substitutes = new List<Player>
        {
            CreatePlayer("Sub GK", Position.Goalkeeper, isStarter: false)
        };

        for (var index = 1; index < substituteCount; index++)
        {
            substitutes.Add(CreatePlayer($"Sub {index}", Position.Midfielder, isStarter: false));
        }

        return new Team
        {
            Name = name,
            Formation = "4-3-3",
            Players = players,
            Substitutes = substitutes
        };
    }

    private static Player CreatePlayer(string name, Position position, bool isStarter)
    {
        return new Player
        {
            Name = name,
            Position = position,
            OverallRating = 75,
            Form = "Average",
            IsStarter = isStarter,
            IsOnPitch = isStarter,
            Attack = 75,
            Defense = 75,
            Passing = 75,
            Stamina = 75,
            CurrentStamina = 75,
            Finishing = 75
        };
    }

    private static Player CreateExactPlayer(string name, Position position, string preferredPosition, int overall, bool isStarter)
    {
        var player = CreatePlayer(name, position, isStarter);
        player.PreferredPosition = preferredPosition;
        player.AssignedPosition = preferredPosition;
        player.OverallRating = overall;
        player.BaseOverallRating = overall;
        return player;
    }
}
