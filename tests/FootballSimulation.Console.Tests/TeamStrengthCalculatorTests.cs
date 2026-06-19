using FootballSimulation.Data;
using FootballSimulation.Engine;
using FootballSimulation.Models;

namespace FootballSimulation.Console.Tests;

public class TeamStrengthCalculatorTests
{
    [Fact]
    public void CalculateAttackStrength_UsesFormationModifier()
    {
        var seedDataService = new SeedDataService();
        var calculator = new TeamStrengthCalculator();
        var team = seedDataService.CreateHomeTeam();

        team.Formation = "4-4-2";
        var balancedAttack = calculator.CalculateAttackStrength(team);

        team.Formation = "4-3-3";
        var attackingAttack = calculator.CalculateAttackStrength(team);

        Assert.True(attackingAttack > balancedAttack);
    }

    [Fact]
    public void CalculateDefenseStrength_DropsAfterSendingOffAPlayer()
    {
        var seedDataService = new SeedDataService();
        var calculator = new TeamStrengthCalculator();
        var team = seedDataService.CreateAwayTeam();

        var fullStrengthDefense = calculator.CalculateDefenseStrength(team);
        team.Players.First(player => player.Position == Position.Defender).IsSentOff = true;
        var reducedDefense = calculator.CalculateDefenseStrength(team);

        Assert.True(reducedDefense < fullStrengthDefense);
        Assert.True(reducedDefense < fullStrengthDefense * 0.85);
    }

    [Fact]
    public void CalculateAttackStrength_DropsHeavilyAfterSendingOffAPlayer()
    {
        var seedDataService = new SeedDataService();
        var calculator = new TeamStrengthCalculator();
        var team = seedDataService.CreateHomeTeam();
        team.Tactics = new TeamTactics
        {
            Mentality = Mentality.AllOutAttack,
            Tempo = 90,
            PressingIntensity = 90,
            Width = 80,
            DefensiveLine = 75
        };

        var fullStrengthAttack = calculator.CalculateAttackStrength(team);
        team.Players.First(player => player.Position != Position.Goalkeeper).IsSentOff = true;
        var reducedAttack = calculator.CalculateAttackStrength(team);

        Assert.True(reducedAttack < fullStrengthAttack * 0.70);
    }

    [Fact]
    public void CalculateAttackStrength_IncreasesWithAttackingTactics()
    {
        var seedDataService = new SeedDataService();
        var calculator = new TeamStrengthCalculator();
        var team = seedDataService.CreateHomeTeam();

        team.Tactics = new TeamTactics
        {
            Mentality = Mentality.Balanced,
            PressingIntensity = 50,
            Width = 50,
            Tempo = 50,
            DefensiveLine = 50
        };
        var balancedAttack = calculator.CalculateAttackStrength(team);

        team.Tactics = new TeamTactics
        {
            Mentality = Mentality.Attacking,
            PressingIntensity = 75,
            Width = 70,
            Tempo = 80,
            DefensiveLine = 60
        };
        var attackingSetup = calculator.CalculateAttackStrength(team);

        Assert.True(attackingSetup > balancedAttack);
    }

    [Fact]
    public void CalculateDefenseStrength_DropsForInjuredOrFatiguedPlayers()
    {
        var seedDataService = new SeedDataService();
        var calculator = new TeamStrengthCalculator();
        var team = seedDataService.CreateAwayTeam();

        var healthyDefense = calculator.CalculateDefenseStrength(team);

        foreach (var player in team.Players)
        {
            player.Fatigue = 80;
            player.IsInjured = true;
        }

        var reducedDefense = calculator.CalculateDefenseStrength(team);

        Assert.True(reducedDefense < healthyDefense);
    }
}
