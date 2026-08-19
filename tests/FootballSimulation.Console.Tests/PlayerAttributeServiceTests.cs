using FootballSimulation.Engine;
using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class PlayerAttributeServiceTests
{
    [Fact]
    public void GetAttributes_NormalizesLowGoalkeeperDefendingToOverallLevel()
    {
        var goalkeeper = CreateGoalkeeper();

        var attributes = PlayerAttributeService.GetAttributes(goalkeeper);

        Assert.InRange(attributes.Defending, goalkeeper.OverallRating, 99);
        Assert.InRange(attributes.Physical, goalkeeper.OverallRating - 4, 99);
    }

    [Fact]
    public void ApplyMissingAttributes_RepairsExistingGoalkeeperData()
    {
        var goalkeeper = CreateGoalkeeper();

        PlayerAttributeService.ApplyMissingAttributes(goalkeeper);

        Assert.InRange(goalkeeper.Defending, goalkeeper.OverallRating, 99);
        Assert.InRange(goalkeeper.Physical, goalkeeper.OverallRating - 4, 99);
    }

    [Fact]
    public void GoalkeeperOverallMateriallyImprovesGoalkeepingContestScore()
    {
        var calculator = new ContextualPlayerPerformanceCalculator();
        var lowerRated = CreateGoalkeeper(overall: 72);
        var elite = CreateGoalkeeper(overall: 88);

        var lowerScore = calculator.Calculate(lowerRated, MatchActionType.Goalkeeping).Score;
        var eliteScore = calculator.Calculate(elite, MatchActionType.Goalkeeping).Score;

        Assert.True(eliteScore >= lowerScore + 10);
    }

    [Fact]
    public void GetAttributes_DoesNotTurnOutfieldDefendingIntoOverallRating()
    {
        var forward = new Player
        {
            Name = "Forward",
            Position = Position.Forward,
            PreferredPosition = "ST",
            OverallRating = 88,
            Pace = 90,
            Shooting = 90,
            Passing = 80,
            Dribbling = 88,
            Defending = 35,
            Physical = 82,
            Stamina = 100
        };

        var attributes = PlayerAttributeService.GetAttributes(forward);

        Assert.Equal(35, attributes.Defending);
    }

    private static Player CreateGoalkeeper(int overall = 88)
    {
        return new Player
        {
            Name = "Test Goalkeeper",
            Position = Position.Goalkeeper,
            PreferredPosition = "GK",
            OverallRating = overall,
            Pace = 44,
            Shooting = 40,
            Passing = 76,
            Dribbling = 46,
            Defending = 48,
            Physical = 80,
            Stamina = 100,
            CurrentForm = 50,
            Morale = 50
        };
    }
}
