using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class FatigueBadgeServiceTests
{
    [Theory]
    [InlineData(82, 4, 45)]
    [InlineData(76, 5, 55)]
    public void Evaluate_DoesNotFlagNormalRegularStarterLoad(int stamina, int recentLoad, int seasonFatigue)
    {
        var player = CreatePlayer(stamina, recentLoad, seasonFatigue);

        var badge = FatigueBadgeService.Evaluate(player);

        Assert.Equal(string.Empty, badge.Text);
    }

    [Fact]
    public void Evaluate_ShowsTiredOnlyForMeaningfulFatigue()
    {
        var player = CreatePlayer(stamina: 72, recentLoad: 6, seasonFatigue: 68);

        var badge = FatigueBadgeService.Evaluate(player);

        Assert.Equal("Tired", badge.Text);
        Assert.Contains("Stamina 72%", badge.Tooltip);
    }

    [Fact]
    public void Evaluate_ShowsRiskForLowStaminaWithHighFatigue()
    {
        var player = CreatePlayer(stamina: 52, recentLoad: 6, seasonFatigue: 70);

        var badge = FatigueBadgeService.Evaluate(player);

        Assert.Equal("Risk", badge.Text);
        Assert.Contains("Increased injury risk", badge.Tooltip);
    }

    [Fact]
    public void Evaluate_ShowsTiredInsteadOfRiskForHighHalftimeStaminaWithHeavySeasonFatigue()
    {
        var player = CreatePlayer(stamina: 92, recentLoad: 5, seasonFatigue: 90);

        var badge = FatigueBadgeService.Evaluate(player);

        Assert.Equal("Tired", badge.Text);
        Assert.Contains("Stamina 92%", badge.Tooltip);
    }

    [Fact]
    public void Evaluate_ShowsTiredInsteadOfRiskForVeryHighHalftimeStaminaWithExtremeSeasonFatigue()
    {
        var player = CreatePlayer(stamina: 94, recentLoad: 5, seasonFatigue: 96);

        var badge = FatigueBadgeService.Evaluate(player);

        Assert.Equal("Tired", badge.Text);
        Assert.Contains("Stamina 94%", badge.Tooltip);
        Assert.Contains("season fatigue 96", badge.Tooltip);
    }

    [Fact]
    public void Evaluate_ShowsRiskForHeavySeasonFatigueOnlyAfterStaminaDropsFurther()
    {
        var player = CreatePlayer(stamina: 72, recentLoad: 5, seasonFatigue: 90);

        var badge = FatigueBadgeService.Evaluate(player);

        Assert.Equal("Risk", badge.Text);
    }

    [Fact]
    public void Evaluate_FullStaminaUsuallyClearsFatigueBadges()
    {
        var player = CreatePlayer(stamina: 100, recentLoad: 7, seasonFatigue: 84);
        player.ConsecutiveStarts = 9;
        player.RecentMatchMinutes = [90, 90, 90, 90, 90];
        player.ConsecutiveFullMatches = 5;

        var badge = FatigueBadgeService.Evaluate(player, fixtureGapDays: 2);

        Assert.Equal(string.Empty, badge.Text);
    }

    [Fact]
    public void Evaluate_FullStaminaShowsTiredForExtremeSeasonFatigue()
    {
        var player = CreatePlayer(stamina: 100, recentLoad: 4, seasonFatigue: 92);

        var badge = FatigueBadgeService.Evaluate(player);

        Assert.Equal("Tired", badge.Text);
        Assert.Contains("season fatigue 92", badge.Tooltip);
    }

    [Fact]
    public void Evaluate_FullStaminaKeepsExtremeConsecutiveStartLoad()
    {
        var player = CreatePlayer(stamina: 100, recentLoad: 4, seasonFatigue: 45);
        player.ConsecutiveStarts = 12;

        var badge = FatigueBadgeService.Evaluate(player);

        Assert.Equal("Load", badge.Text);
    }

    [Fact]
    public void Evaluate_LongRestSuppressesTiredUnlessStaminaIsLow()
    {
        var player = CreatePlayer(stamina: 78, recentLoad: 6, seasonFatigue: 70);

        var badge = FatigueBadgeService.Evaluate(player, fixtureGapDays: 5);

        Assert.Equal(string.Empty, badge.Text);
    }

    [Fact]
    public void Evaluate_RestedLastMatchReducesEffectiveRecentLoad()
    {
        var player = CreatePlayer(stamina: 79, recentLoad: 7, seasonFatigue: 50);
        player.RecentMatchMinutes = [90, 90, 90, 90, 0];

        var badge = FatigueBadgeService.Evaluate(player);

        Assert.Equal(string.Empty, badge.Text);
    }

    [Fact]
    public void Evaluate_ShortRestAfterThreeFullMatchesShowsTired()
    {
        var player = CreatePlayer(stamina: 82, recentLoad: 4, seasonFatigue: 45);
        player.RecentMatchMinutes = [90, 90, 90];
        player.ConsecutiveFullMatches = 3;

        var badge = FatigueBadgeService.Evaluate(player, fixtureGapDays: 3);

        Assert.Equal("Tired", badge.Text);
    }

    [Fact]
    public void CalculateWorkloadRiskPercentage_IncreasesWithLowStaminaAndRecentLoad()
    {
        var freshPlayer = CreatePlayer(stamina: 96, recentLoad: 1, seasonFatigue: 10);
        var riskyPlayer = CreatePlayer(stamina: 52, recentLoad: 7, seasonFatigue: 45);
        riskyPlayer.RecentMatchMinutes = [90, 90, 90, 80, 70];
        riskyPlayer.ConsecutiveStarts = 8;
        riskyPlayer.ConsecutiveFullMatches = 3;

        var freshRisk = FatigueBadgeService.CalculateWorkloadRiskPercentage(freshPlayer, fixtureGapDays: 6);
        var workloadRisk = FatigueBadgeService.CalculateWorkloadRiskPercentage(riskyPlayer, fixtureGapDays: 3);

        Assert.InRange(freshRisk, 0, 20);
        Assert.InRange(workloadRisk, 70, 100);
        Assert.True(workloadRisk > freshRisk);
    }

    [Fact]
    public void CalculateWorkloadRiskPercentage_IsNotJustSeasonFatigue()
    {
        var lowSeasonFatiguePlayer = CreatePlayer(stamina: 45, recentLoad: 6, seasonFatigue: 26);

        var risk = FatigueBadgeService.CalculateWorkloadRiskPercentage(lowSeasonFatiguePlayer);

        Assert.True(risk > lowSeasonFatiguePlayer.SeasonFatigue);
    }

    private static Player CreatePlayer(int stamina, int recentLoad, int seasonFatigue)
    {
        return new Player
        {
            Name = "Test Player",
            Position = Position.Midfielder,
            Stamina = stamina,
            MatchesPlayedRecently = recentLoad,
            SeasonFatigue = seasonFatigue
        };
    }
}
