using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class PositionCompatibilityServiceTests
{
    [Theory]
    [InlineData("Joao Pedro", "ST/CAM", "ST")]
    [InlineData("Cole Palmer", "cam/rw", "CAM")]
    [InlineData("Wide Player", "RW/LW", "LW")]
    [InlineData("Midfielder", "CM, CDM", "CDM")]
    [InlineData("Defender", "LB CB", "CB")]
    [InlineData("Hybrid", "ST | CAM", "CAM")]
    public void CanPlayPosition_HandlesMultiPositionText(string playerName, string positionText, string targetSlot)
    {
        var player = CreatePlayer(playerName, Position.Forward, positionText);

        Assert.True(PositionCompatibilityService.CanPlayPosition(player, targetSlot));
        Assert.True(PositionCompatibilityService.GetCompatibilityScore(player, targetSlot) >= PositionCompatibilityService.Emergency);
    }

    [Theory]
    [InlineData("Reece James", "RB", "RB")]
    [InlineData("Malo Gusto", "RB", "RB")]
    public void CanPlayPosition_HandlesSingleNaturalPosition(string playerName, string positionText, string targetSlot)
    {
        var player = CreatePlayer(playerName, Position.Defender, positionText);

        Assert.True(PositionCompatibilityService.CanPlayPosition(player, targetSlot));
        Assert.Equal(100, PositionCompatibilityService.GetCompatibilityScore(player, targetSlot));
    }

    [Fact]
    public void CanOccupySlot_AllowsOutfieldOutOfPositionWhenDegradedSelectionIsAllowed()
    {
        var delap = CreatePlayer("Liam Delap", Position.Forward, "ST");
        var centerBack = CreatePlayer("Center Back", Position.Defender, "CB");

        Assert.False(PositionCompatibilityService.CanPlayPosition(delap, "CAM"));
        Assert.Equal(PositionCompatibilityService.Impossible, PositionCompatibilityService.GetCompatibilityScore(delap, "CAM"));
        Assert.True(PositionCompatibilityService.CanOccupySlot(delap, "CAM", allowOutOfPosition: true));
        Assert.True(PositionCompatibilityService.CanOccupySlot(centerBack, "ST", allowOutOfPosition: true));
        Assert.False(PositionCompatibilityService.CanOccupySlot(centerBack, "ST", allowOutOfPosition: false));
    }

    [Fact]
    public void CanOccupySlot_BlocksGoalkeeperOutfieldMismatch()
    {
        var goalkeeper = CreatePlayer("Goalkeeper", Position.Goalkeeper, "GK");
        var striker = CreatePlayer("Striker", Position.Forward, "ST");

        Assert.False(PositionCompatibilityService.CanOccupySlot(goalkeeper, "ST", allowOutOfPosition: true));
        Assert.False(PositionCompatibilityService.CanOccupySlot(striker, "GK", allowOutOfPosition: true));
    }

    [Fact]
    public void EnsurePositionMetadata_PreservesSecondaryPositionsFromCombinedText()
    {
        var player = CreatePlayer("Joao Pedro", Position.Forward, "ST/CAM");

        PositionSuitabilityService.EnsurePositionMetadata(player, "CAM");

        Assert.Equal("ST", player.PreferredPosition);
        Assert.Contains("CAM", player.SecondaryPositions);
        Assert.Equal("CAM", player.AssignedPosition);
        Assert.Equal(1.0, PositionSuitabilityService.GetEffectivenessMultiplier(player));
    }

    [Theory]
    [InlineData("ST")]
    [InlineData("CAM")]
    public void CenterForward_FullyAdaptsToStrikerAndAttackingMidfielder(string targetSlot)
    {
        var player = CreatePlayer("Center Forward", Position.Forward, "CF");

        PositionSuitabilityService.EnsurePositionMetadata(player, targetSlot);

        Assert.True(PositionCompatibilityService.CanPlayPosition(player, targetSlot));
        Assert.Equal(100, PositionCompatibilityService.GetCompatibilityScore(player, targetSlot));
        Assert.Equal(0, PositionSuitabilityService.GetOutOfPositionPenalty(player, targetSlot));
        Assert.Equal(1.0, PositionSuitabilityService.GetEffectivenessMultiplier(player));
        Assert.False(PositionSuitabilityService.IsOutOfPosition(player));
    }

    private static Player CreatePlayer(string name, Position position, string preferredPosition)
    {
        return new Player
        {
            Name = name,
            Position = position,
            PreferredPosition = preferredPosition,
            OverallRating = 75
        };
    }
}
