using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class FormationSystemTests
{
    [Fact]
    public void FormationCatalog_ContainsRequestedCategoriesAndElevenSlots()
    {
        var formations = FormationCatalogService.GetFormations();

        Assert.Contains(formations, formation => formation.Category == FormationCategory.Attacking && formation.Name == "4-2-4");
        Assert.Contains(formations, formation => formation.Category == FormationCategory.Balanced && formation.Name == "4-3-3 Holding");
        Assert.Contains(formations, formation => formation.Category == FormationCategory.Defensive && formation.Name == "5-4-1");
        Assert.All(formations, formation => Assert.Equal(11, FormationSlotService.GetSlots(formation.Name).Count));
    }

    [Theory]
    [InlineData("4-3-3", "4-3-3 Attack")]
    [InlineData("4-2-3-1", "4-2-3-1 Wide")]
    [InlineData("  4-4-2  ", "4-4-2")]
    public void NormalizeFormationName_KeepsLegacySavesCompatible(string input, string expected)
    {
        Assert.Equal(expected, FormationCatalogService.NormalizeFormationName(input));
    }

    [Fact]
    public void FormationPresetService_AllowsFiveSavedSetups()
    {
        var service = new FormationPresetService();
        var team = CreateTeam("Preset FC", "4-3-3 Holding");

        foreach (var name in FormationPresetService.SuggestedPresetNames)
        {
            var result = service.SaveNewPreset(team, name);
            Assert.True(result.Success);
        }

        var rejected = service.SaveNewPreset(team, "Sixth");

        Assert.Equal(5, team.FormationPresets.Count);
        Assert.False(rejected.Success);
    }

    [Fact]
    public void AiLineupSelection_TopTeamsPreferModernAttackingShapes()
    {
        var team = CreateTeam("Elite FC", "4-4-2", overall: 86);

        var formation = AiLineupSelectionService.SelectPreferredFormation(team);

        Assert.Contains(formation, new[] { "4-3-3 Holding", "4-2-3-1 Wide", "4-3-3 Attack" });
    }

    [Fact]
    public void AiLineupSelection_ProtectingLeadPrefersBackFive()
    {
        var team = CreateTeam("Low Block FC", "4-3-3", overall: 78);
        team.Tactics.Mentality = Mentality.Defensive;

        var formation = AiLineupSelectionService.SelectPreferredFormation(team);

        Assert.Contains(formation, new[] { "5-4-1", "5-3-2" });
    }

    [Fact]
    public void InMatchFormationService_ApplyFormationPreservesOnFieldPlayersAndKeepsRoles()
    {
        var team = CreateTeam("Live Switch FC", "4-2-3-1", overall: 80);
        team.Players[7] = CreatePlayer("Attacking Midfielder", Position.Midfielder, "CAM", 80);
        var originalNames = team.Players.Where(player => player.IsOnPitch).Select(player => player.Name).Order().ToList();
        var service = new InMatchFormationService();

        var result = service.ApplyFormation(team, "4-3-3 Attack");

        Assert.True(result.Success, string.Join("; ", result.Warnings));
        Assert.Equal("4-3-3 Attack", team.Formation);
        Assert.Equal(originalNames, team.Players.Where(player => player.IsOnPitch).Select(player => player.Name).Order().ToList());
        Assert.Equal("GK", team.Players.Single(player => player.Name == "Goalkeeper").AssignedPosition);
        Assert.Equal("ST", team.Players.Single(player => player.Name == "Striker").AssignedPosition);
        Assert.Equal("LW", team.Players.Single(player => player.Name == "Left Winger").AssignedPosition);
        Assert.Equal("RW", team.Players.Single(player => player.Name == "Right Winger").AssignedPosition);
        Assert.DoesNotContain(team.Players, player =>
            player.Position == Position.Forward &&
            player.AssignedPosition is "CB" or "LB" or "RB" or "LWB" or "RWB");
    }

    [Fact]
    public void NaturalPositionAssignment_Fills4231ByPrimaryAndSecondaryPositions()
    {
        var players = new[]
        {
            CreatePlayer("Robert Sanchez", Position.Goalkeeper, "GK", 80),
            CreatePlayer("Marc Cucurella", Position.Defender, "LB", 82),
            CreatePlayer("Levi Colwill", Position.Defender, "CB", 84),
            CreatePlayer("Trevoh Chalobah", Position.Defender, "CB", 80),
            CreatePlayer("Reece James", Position.Defender, "RB", 86, ["RWB", "CB"]),
            CreatePlayer("Moises Caicedo", Position.Midfielder, "CDM", 87, ["CM"]),
            CreatePlayer("Enzo Fernandez", Position.Midfielder, "CM", 86, ["CDM"]),
            CreatePlayer("Alejandro Garnacho", Position.Forward, "LW", 82),
            CreatePlayer("Cole Palmer", Position.Midfielder, "CAM", 88, ["RW"]),
            CreatePlayer("Pedro Neto", Position.Forward, "RW", 82, ["LW"]),
            CreatePlayer("Liam Delap", Position.Forward, "ST", 80)
        }.OrderBy(player => player.Name).ToList();
        var slots = FormationSlotService.GetSlots("4-2-3-1 Wide");

        var result = NaturalPositionAssignmentService.Assign(players, slots);

        Assert.True(result.Success, string.Join("; ", result.Warnings));
        Assert.Equal("ST", result.Assignments.Single(item => item.Player.Name == "Liam Delap").Slot);
        Assert.Equal("CAM", result.Assignments.Single(item => item.Player.Name == "Cole Palmer").Slot);
        Assert.Equal("LW", result.Assignments.Single(item => item.Player.Name == "Alejandro Garnacho").Slot);
        Assert.Equal("RW", result.Assignments.Single(item => item.Player.Name == "Pedro Neto").Slot);
        Assert.Equal("CDM", result.Assignments.Single(item => item.Player.Name == "Moises Caicedo").Slot);
        Assert.Equal("CDM", result.Assignments.Single(item => item.Player.Name == "Enzo Fernandez").Slot);
        Assert.Equal("RB", result.Assignments.Single(item => item.Player.Name == "Reece James").Slot);
        Assert.DoesNotContain(result.Assignments, item => item.Player.Position == Position.Forward && item.Slot is "CB" or "CDM");
    }

    [Fact]
    public void InMatchFormationService_RecalculatesRolesAfterFormationChange()
    {
        var team = CreateTeam("Switch FC", "4-3-3 Holding", overall: 80);
        team.Players = team.Players.OrderByDescending(player => player.OverallRating).ToList();
        foreach (var player in team.Players)
        {
            player.AssignedPosition = "CM";
        }

        var service = new InMatchFormationService();
        var result = service.ApplyFormation(team, "3-5-2");

        Assert.True(result.Success, string.Join("; ", result.Warnings));
        Assert.Equal("3-5-2", team.Formation);
        Assert.Equal(3, team.Players.Count(player => player.AssignedPosition == "CB"));
        Assert.Equal(2, team.Players.Count(player => player.AssignedPosition == "ST"));
        Assert.DoesNotContain(team.Players, player => player.Position == Position.Forward && player.AssignedPosition is "CB" or "CDM");
        Assert.DoesNotContain(team.Players, player => player.Position == Position.Defender && player.AssignedPosition == "ST");
    }

    [Fact]
    public void PositionSuitability_UsesSpecificOutOfPositionPenalties()
    {
        var cm = CreatePlayer("Central Midfielder", Position.Midfielder, "CM", 80);
        var cam = CreatePlayer("Attacking Midfielder", Position.Midfielder, "CAM", 80);
        var rb = CreatePlayer("Right Back", Position.Defender, "RB", 80);
        var striker = CreatePlayer("Striker", Position.Forward, "ST", 80);

        Assert.Equal(2, PositionSuitabilityService.GetOutOfPositionPenalty(cm, "CAM"));
        Assert.Equal(3, PositionSuitabilityService.GetOutOfPositionPenalty(cam, "CM"));
        Assert.Equal(4, PositionSuitabilityService.GetOutOfPositionPenalty(rb, "CB"));
        Assert.Equal(5, PositionSuitabilityService.GetOutOfPositionPenalty(striker, "RW"));
        Assert.Equal(99, PositionSuitabilityService.GetOutOfPositionPenalty(striker, "CDM"));
        cm.AssignedPosition = "CAM";
        Assert.Equal(78, PositionSuitabilityService.GetEffectiveOverall(cm));
    }

    [Fact]
    public void InMatchFormationService_RejectsFormationThatWouldForceAttackersIntoDefense()
    {
        var team = new Team
        {
            Name = "Unbalanced FC",
            Formation = "4-2-4",
            Players =
            [
                CreatePlayer("Goalkeeper", Position.Goalkeeper, "GK", 75),
                CreatePlayer("Only Defender", Position.Defender, "CB", 75),
                CreatePlayer("Striker A", Position.Forward, "ST", 75),
                CreatePlayer("Striker B", Position.Forward, "ST", 75),
                CreatePlayer("Striker C", Position.Forward, "ST", 75),
                CreatePlayer("Left Winger A", Position.Forward, "LW", 75),
                CreatePlayer("Left Winger B", Position.Forward, "LW", 75),
                CreatePlayer("Right Winger A", Position.Forward, "RW", 75),
                CreatePlayer("Right Winger B", Position.Forward, "RW", 75),
                CreatePlayer("Forward A", Position.Forward, "CF", 75),
                CreatePlayer("Forward B", Position.Forward, "CF", 75)
            ]
        };
        var service = new InMatchFormationService();

        var result = service.EvaluateFormation(team, "5-4-1");

        Assert.False(result.Success);
    }

    private static Team CreateTeam(string name, string formation, int overall = 78)
    {
        var players = new[]
        {
            CreatePlayer("Goalkeeper", Position.Goalkeeper, "GK", overall),
            CreatePlayer("Left Back", Position.Defender, "LB", overall),
            CreatePlayer("Center Back A", Position.Defender, "CB", overall),
            CreatePlayer("Center Back B", Position.Defender, "CB", overall),
            CreatePlayer("Right Back", Position.Defender, "RB", overall),
            CreatePlayer("Defensive Midfielder", Position.Midfielder, "CDM", overall),
            CreatePlayer("Central Midfielder A", Position.Midfielder, "CM", overall),
            CreatePlayer("Central Midfielder B", Position.Midfielder, "CM", overall),
            CreatePlayer("Left Winger", Position.Forward, "LW", overall),
            CreatePlayer("Striker", Position.Forward, "ST", overall),
            CreatePlayer("Right Winger", Position.Forward, "RW", overall)
        };

        return new Team
        {
            Name = name,
            Formation = formation,
            Players = players.ToList(),
            Substitutes =
            [
                CreatePlayer("Sub Goalkeeper", Position.Goalkeeper, "GK", overall - 5),
                CreatePlayer("Sub Defender", Position.Defender, "CB", overall - 5),
                CreatePlayer("Sub Midfielder", Position.Midfielder, "CM", overall - 5),
                CreatePlayer("Sub Forward", Position.Forward, "ST", overall - 5)
            ]
        };
    }

    private static Player CreatePlayer(string name, Position position, string exactPosition, int overall, List<string>? secondaryPositions = null)
    {
        return new Player
        {
            Name = name,
            PlayerId = Guid.NewGuid().ToString("N"),
            Position = position,
            PreferredPosition = exactPosition,
            AssignedPosition = exactPosition,
            OverallRating = overall,
            BaseOverallRating = overall,
            SecondaryPositions = secondaryPositions ?? [],
            Stamina = 90,
            IsStarter = true,
            IsOnPitch = true
        };
    }
}
