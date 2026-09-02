using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class FutureStarMarketServiceTests
{
    [Fact]
    public void TryGenerateSeasonalFutureStar_AddsRareProspectToSearchableFreeAgentMarket()
    {
        var state = new TransferMarketState { ActiveSeason = "2029-30" };
        var service = new FutureStarMarketService();

        var futureStar = service.TryGenerateSeasonalFutureStar(state, "2029-30", generationChance: 1);

        Assert.NotNull(futureStar);
        Assert.Same(futureStar, Assert.Single(state.FreeAgents));
        Assert.StartsWith("future-star-2029-30-", futureStar.PlayerId);
        Assert.InRange(futureStar.Age.GetValueOrDefault(), 16, 19);
        Assert.InRange(futureStar.OverallRating, 62, 73);
        Assert.InRange(futureStar.PotentialOverall.GetValueOrDefault(), 89, 96);
        Assert.Equal(PlayerRole.Prospect, futureStar.Role);
        Assert.Equal(PlayerContractStatus.FreeAgent, futureStar.ContractStatus);

        var listings = new TransferMarketService().SearchPlayers(
            state,
            new TransferSearchCriteria { PlayerName = futureStar.Name });
        var listing = Assert.Single(listings);
        Assert.Same(futureStar, listing.Player);
        Assert.Equal("Free Agents", listing.Team.Name);
    }

    [Fact]
    public void TryGenerateSeasonalFutureStar_DoesNotDuplicateSameSeasonPlayer()
    {
        var state = new TransferMarketState { ActiveSeason = "2029-30" };
        var service = new FutureStarMarketService();

        var first = service.TryGenerateSeasonalFutureStar(state, "2029-30", generationChance: 1);
        var duplicate = service.TryGenerateSeasonalFutureStar(state, "2029-30", generationChance: 1);

        Assert.NotNull(first);
        Assert.Null(duplicate);
        Assert.Single(state.FreeAgents);
    }

    [Fact]
    public void TryGenerateSeasonalFutureStar_DefaultChanceCreatesOccasionalNotAnnualProspects()
    {
        var service = new FutureStarMarketService();
        var generatedCount = 0;

        foreach (var startYear in Enumerable.Range(2026, 20))
        {
            var season = $"{startYear}-{(startYear + 1) % 100:00}";
            var state = new TransferMarketState { ActiveSeason = season };
            if (service.TryGenerateSeasonalFutureStar(state, season) is not null)
            {
                generatedCount++;
            }
        }

        Assert.InRange(generatedCount, 2, 12);
    }
}
