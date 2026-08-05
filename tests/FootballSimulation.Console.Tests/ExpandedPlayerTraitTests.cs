using FootballSimulation.Data.JsonModels;
using FootballSimulation.Engine;
using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class ExpandedPlayerTraitTests
{
    public static IEnumerable<object[]> ExpandedTraits()
    {
        return Enum.GetValues<PlayerTrait>()
            .Where(ExpandedPlayerTraitService.IsExpandedTrait)
            .Distinct()
            .Select(trait => new object[] { trait });
    }

    [Theory]
    [MemberData(nameof(ExpandedTraits))]
    public void ExpandedTrait_HasDisplayDefinition(PlayerTrait trait)
    {
        var definition = PlayerTraitDisplayService.GetDefinition(trait);

        Assert.False(string.IsNullOrWhiteSpace(definition.Icon));
        Assert.False(string.IsNullOrWhiteSpace(definition.Label));
        Assert.DoesNotContain("Special trait affects", definition.Description);
    }

    [Fact]
    public void Inference_IsDeterministicIdempotentAndAddsOnlyOneTrait()
    {
        var player = CreatePlayer("Test Midfielder", "CM", Position.Midfielder, 84);
        player.Dribbling = 90;
        player.Passing = 88;
        player.Traits = [PlayerTrait.Playmaker];

        var firstResult = ExpandedPlayerTraitService.ApplyInferredTrait(player);
        var secondResult = ExpandedPlayerTraitService.ApplyInferredTrait(player);

        Assert.True(firstResult);
        Assert.False(secondResult);
        Assert.Equal(2, player.Traits.Count);
        Assert.Equal(PlayerTrait.FirstTouch, player.Traits[1]);
    }

    [Fact]
    public void Inference_RespectsRatingAndTraitCountLimits()
    {
        var lowRated = CreatePlayer("Prospect", "ST", Position.Forward, 77);
        lowRated.Physical = 95;
        var capped = CreatePlayer("Capped", "CM", Position.Midfielder, 80);
        capped.Traits = [PlayerTrait.Playmaker, PlayerTrait.Engine, PlayerTrait.TeamPlayer, PlayerTrait.BoxToBox];

        Assert.False(ExpandedPlayerTraitService.ApplyInferredTrait(lowRated));
        Assert.False(ExpandedPlayerTraitService.ApplyInferredTrait(capped));
    }

    [Fact]
    public void TraitLimits_ReserveAdditionalCapacityForHighPotentialAndSuperstars()
    {
        var normal = CreatePlayer("Normal", "CM", Position.Midfielder, 80);
        normal.Age = 22;
        normal.PotentialOverall = 84;
        var eliteProspect = CreatePlayer("Elite Prospect", "CM", Position.Midfielder, 80);
        eliteProspect.Age = 20;
        eliteProspect.PotentialOverall = 92;
        var superstar = CreatePlayer("Superstar", "ST", Position.Forward, 92);

        Assert.Equal(3, ExpandedPlayerTraitService.GetMaximumTraitCount(normal));
        Assert.Equal(4, ExpandedPlayerTraitService.GetMaximumTraitCount(eliteProspect));
        Assert.Equal(6, ExpandedPlayerTraitService.GetMaximumTraitCount(superstar));
    }

    [Fact]
    public void LegacyEarlyCrosserData_LoadsAsCrossingSpecialist()
    {
        var record = new PlayerDataRecord
        {
            Name = "Legacy Winger",
            Position = "RW",
            PreferredPosition = "RW",
            OverallRating = 76,
            Traits = ["EarlyCrosser"]
        };

        var player = new PlayerStatMappingService().MapToPlayer(record);

        Assert.Single(player.Traits);
        Assert.Equal(PlayerTrait.CrossingSpecialist, player.Traits[0]);
        Assert.Equal("Crossing Specialist", PlayerTraitDisplayService.GetLabel(player.Traits[0]));
    }

    [Fact]
    public void ExistingSaveModric_ReceivesFullCuratedProfileIdempotently()
    {
        var modric = CreatePlayer("Luka Modrić", "CM", Position.Midfielder, 83);
        modric.Traits = [PlayerTrait.Playmaker];

        var changed = ExpandedPlayerTraitService.ApplyInferredTrait(modric);
        var changedAgain = ExpandedPlayerTraitService.ApplyInferredTrait(modric);

        Assert.True(changed);
        Assert.False(changedAgain);
        Assert.Equal(
            [PlayerTrait.Playmaker, PlayerTrait.LongPasser, PlayerTrait.PressResistant, PlayerTrait.FirstTouch, PlayerTrait.Composed],
            modric.Traits);
    }

    [Fact]
    public void Mapping_AppliesCuratedTraitWithoutEditingSeedJson()
    {
        var record = new PlayerDataRecord
        {
            Name = "Erling Haaland",
            Position = "ST",
            PreferredPosition = "ST",
            OverallRating = 91,
            Physical = 92,
            Shooting = 92,
            Dribbling = 82,
            Pace = 88,
            PassingAttribute = 75,
            Defending = 40,
            Traits = ["ClinicalFinisher"]
        };

        var player = new PlayerStatMappingService().MapToPlayer(record);

        Assert.Contains(PlayerTrait.Strong, player.Traits);
        Assert.Contains(PlayerTrait.ClinicalFinisher, player.Traits);
    }

    [Fact]
    public void ContextualTraits_OnlyAffectMatchingActionsAndKeepTwoTraitCap()
    {
        var calculator = new ContextualPlayerPerformanceCalculator();
        var player = CreatePlayer("Physical Forward", "ST", Position.Forward, 84);
        player.Traits = [PlayerTrait.Strong, PlayerTrait.TargetForward, PlayerTrait.AerialThreat];

        var physical = calculator.Calculate(player, MatchActionType.PhysicalDuel);
        var passing = calculator.Calculate(player, MatchActionType.Passing);

        Assert.Equal(2, physical.AppliedTraits.Count);
        Assert.Contains(PlayerTrait.Strong, physical.AppliedTraits);
        Assert.DoesNotContain(PlayerTrait.Strong, passing.AppliedTraits);
        Assert.Empty(passing.AppliedTraits);
    }

    [Fact]
    public void StrongTargetForward_ProducesContextualHoldUpFeedBadge()
    {
        var carrier = CreatePlayer("Powerful Striker", "ST", Position.Forward, 85);
        carrier.Traits = [PlayerTrait.Strong, PlayerTrait.TargetForward];
        var support = CreatePlayer("Creative Midfielder", "CAM", Position.Midfielder, 82);
        var team = new Team { Name = "Test FC", Players = [carrier, support] };
        var narrative = new AttackNarrativeContext(
            "attack-1", team.Name, "midfield possession", "direct ball", "direct",
            carrier.Name, support.Name, "high pressure", false);

        var matchEvent = new MatchEventFactory().CreateAttackContinuation(
            12, team, carrier, support, narrative, new Random(4));

        Assert.Equal(PlayerTrait.Strong, matchEvent.TriggeredTrait);
        Assert.Contains("strength", matchEvent.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RelentlessPresserAndRecoveryPaceIncreaseStaminaCost()
    {
        var normal = CreatePlayer("Normal", "CM", Position.Midfielder, 82);
        var specialist = CreatePlayer("Specialist", "CM", Position.Midfielder, 82);
        specialist.Traits = [PlayerTrait.RelentlessPresser, PlayerTrait.RecoveryPace];
        var normalTeam = new Team { Name = "Normal FC", Players = [normal], Tactics = new TeamTactics() };
        var specialistTeam = new Team { Name = "Press FC", Players = [specialist], Tactics = new TeamTactics() };
        var service = new FatigueService();

        service.ApplyMinuteFatigue(normalTeam);
        service.ApplyMinuteFatigue(specialistTeam);

        Assert.True(specialist.Stamina < normal.Stamina);
    }

    private static Player CreatePlayer(string name, string exactPosition, Position position, int overall)
    {
        return new Player
        {
            Name = name,
            PreferredPosition = exactPosition,
            AssignedPosition = exactPosition,
            Position = position,
            OverallRating = overall,
            Attack = overall,
            Defense = overall,
            Passing = overall,
            Finishing = overall,
            Pace = overall,
            Shooting = overall,
            Dribbling = overall,
            Defending = overall,
            Physical = overall,
            Stamina = 100,
            CurrentForm = 50,
            Morale = 50,
            IsOnPitch = true
        };
    }
}
