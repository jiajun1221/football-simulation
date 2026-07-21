using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class FreeAgentRegenServiceTests
{
    [Fact]
    public void ProcessSeasonRollover_RetiresGuaranteedOldFreeAgentAndCreatesMatchingRegen()
    {
        var oldFreeAgent = CreateOldFreeAgent(age: 40);
        var state = new TransferMarketState
        {
            ActiveSeason = "2026-27",
            FreeAgents = [oldFreeAgent]
        };

        var result = new FreeAgentRegenService().ProcessSeasonRollover(state, "2026-27");

        var regen = Assert.Single(result.Regens);
        Assert.Single(result.RetiredPlayers);
        Assert.DoesNotContain(state.FreeAgents, player => player.PlayerId == oldFreeAgent.PlayerId);
        Assert.Contains(state.FreeAgents, player => player.PlayerId == regen.PlayerId);
        Assert.StartsWith("regen-free-agent-2026-27-old-free-agent-test", regen.PlayerId);
        Assert.NotEqual(oldFreeAgent.PlayerId, regen.PlayerId);
        Assert.InRange(regen.Age.GetValueOrDefault(), 16, 19);
        Assert.Equal(Position.Forward, regen.Position);
        Assert.Equal("ST", regen.PreferredPosition);
        Assert.Contains("CF", regen.SecondaryPositions);
        Assert.Equal("Brazil", regen.NationalityName);
        Assert.Equal("BR", regen.NationalityCode);
        Assert.Equal("Assets/Flags/brazil.png", regen.FlagImagePath);
        Assert.Equal(PlayerRole.Prospect, regen.Role);
        Assert.Equal(PlayerContractStatus.FreeAgent, regen.ContractStatus);
        Assert.Equal("free-agents", regen.ClubId);
        Assert.InRange(regen.OverallRating, 55, 69);
        Assert.InRange(regen.PotentialOverall.GetValueOrDefault(), 80, 96);
        Assert.True(regen.PotentialOverall >= regen.OverallRating + 6);
        Assert.True(regen.Pace > 0);
        Assert.True(regen.Attack > 0);
        Assert.Contains(state.Inbox, notification =>
            notification.Message.Contains("1 free agent retired", StringComparison.OrdinalIgnoreCase) &&
            notification.Message.Contains("1 young regen entered", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProcessSeasonRollover_KeepsFreeAgentsBelowRetirementAgeFloor()
    {
        var freeAgent = CreateOldFreeAgent(age: 33);
        var state = new TransferMarketState
        {
            ActiveSeason = "2026-27",
            FreeAgents = [freeAgent]
        };

        var result = new FreeAgentRegenService().ProcessSeasonRollover(state, "2026-27");

        Assert.Empty(result.RetiredPlayers);
        Assert.Empty(result.Regens);
        Assert.Same(freeAgent, Assert.Single(state.FreeAgents));
        Assert.Empty(state.Inbox);
    }

    [Fact]
    public void ProcessSeasonRollover_GeneratesDeterministicRegenForSamePlayerAndSeason()
    {
        var firstState = new TransferMarketState
        {
            ActiveSeason = "2026-27",
            FreeAgents = [CreateOldFreeAgent(age: 40)]
        };
        var secondState = new TransferMarketState
        {
            ActiveSeason = "2026-27",
            FreeAgents = [CreateOldFreeAgent(age: 40)]
        };
        var service = new FreeAgentRegenService();

        var firstRegen = service.ProcessSeasonRollover(firstState, "2026-27").Regens.Single();
        var secondRegen = service.ProcessSeasonRollover(secondState, "2026-27").Regens.Single();

        Assert.Equal(firstRegen.PlayerId, secondRegen.PlayerId);
        Assert.Equal(firstRegen.Name, secondRegen.Name);
        Assert.Equal(firstRegen.Age, secondRegen.Age);
        Assert.Equal(firstRegen.OverallRating, secondRegen.OverallRating);
        Assert.Equal(firstRegen.PotentialOverall, secondRegen.PotentialOverall);
        Assert.Equal(firstRegen.Traits, secondRegen.Traits);
    }

    private static Player CreateOldFreeAgent(int age)
    {
        return new Player
        {
            PlayerId = "old-free-agent-test",
            Name = "Old Free Agent Test",
            Position = Position.Forward,
            PreferredPosition = "ST",
            AssignedPosition = "ST",
            SecondaryPositions = ["CF"],
            Nationality = "Brazil",
            NationalityName = "Brazil",
            NationalityCode = "BR",
            FlagImagePath = "Assets/Flags/brazil.png",
            OverallRating = 86,
            BaseOverallRating = 86,
            PotentialOverall = 90,
            Age = age,
            PreferredFoot = "Right",
            Traits = [PlayerTrait.ClinicalFinisher, PlayerTrait.PowerHeader],
            ContractEndYear = 2025,
            ContractStatus = PlayerContractStatus.FreeAgent
        };
    }
}
