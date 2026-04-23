using FootballSimulation.Data;
using FootballSimulation.Engine;
using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class TacticalRealismTests
{
    [Fact]
    public void ClinicalFinisherTrait_IncreasesEffectiveFinishing()
    {
        var calculator = new TeamStrengthCalculator();
        var player = new Player
        {
            Name = "Test Forward",
            Position = Position.Forward,
            Attack = 75,
            Defense = 35,
            Passing = 65,
            Stamina = 80,
            CurrentStamina = 80,
            Finishing = 78,
            CurrentForm = 60,
            Morale = 60
        };

        var baseFinishing = calculator.GetEffectiveFinishing(player);

        player.Traits.Add(PlayerTrait.ClinicalFinisher);
        var traitFinishing = calculator.GetEffectiveFinishing(player);

        Assert.True(traitFinishing > baseFinishing);
    }

    [Fact]
    public void TacticalInsightService_ReturnsOpponentThreatsAndRecommendations()
    {
        var seedDataService = new SeedDataService();
        var selectedTeam = seedDataService.CreateHomeTeam();
        var opponent = seedDataService.CreateAwayTeam();
        opponent.Formation = "4-3-3";
        opponent.Tactics = new TeamTactics
        {
            Mentality = Mentality.Attacking,
            PressingIntensity = 78,
            Width = 65,
            Tempo = 72,
            DefensiveLine = 76
        };
        opponent.Players.First(player => player.Position == Position.Forward).Traits.Add(PlayerTrait.PaceMerchant);

        var service = new TacticalInsightService();

        var insight = service.GenerateInsight(selectedTeam, opponent);

        Assert.NotEmpty(insight.OpponentThreats);
        Assert.NotEmpty(insight.LikelyTactics);
        Assert.NotEmpty(insight.Recommendations);
    }
}
