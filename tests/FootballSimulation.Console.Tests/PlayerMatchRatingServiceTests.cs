using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Console.Tests;

public class PlayerMatchRatingServiceTests
{
    [Fact]
    public void CalculateContextualRating_RewardsDefensiveContributions()
    {
        var match = CreateMatch(homeScore: 0, awayScore: 0);
        var quietDefender = new PlayerMatchPerformance
        {
            PlayerName = "Quiet Defender",
            TeamName = match.HomeTeam.Name,
            Position = Position.Defender,
            Rating = 6.0
        };
        var activeDefender = new PlayerMatchPerformance
        {
            PlayerName = "Active Defender",
            TeamName = match.HomeTeam.Name,
            Position = Position.Defender,
            Rating = 6.0,
            Tackles = 3,
            Interceptions = 2,
            Blocks = 1,
            Clearances = 4
        };

        var quietRating = PlayerMatchRatingService.CalculateContextualRating(match, quietDefender);
        var activeRating = PlayerMatchRatingService.CalculateContextualRating(match, activeDefender);

        Assert.True(activeRating > quietRating);
    }

    [Fact]
    public void CalculateContextualRating_CleanSheetDefenderWithActionsReachesFairRange()
    {
        var match = CreateMatch(homeScore: 1, awayScore: 0);
        match.AwayStats.TotalShots = 5;
        match.AwayStats.ShotsOnTarget = 1;
        match.AwayStats.ExpectedGoals = 0.4;

        var defender = new PlayerMatchPerformance
        {
            PlayerName = "Busy Clean Sheet Defender",
            TeamName = match.HomeTeam.Name,
            Position = Position.Defender,
            Rating = 6.0,
            Tackles = 2,
            Interceptions = 2,
            Blocks = 1,
            Clearances = 3
        };

        var rating = PlayerMatchRatingService.CalculateContextualRating(match, defender);

        Assert.InRange(rating, 6.8, 8.0);
    }

    [Fact]
    public void CalculateContextualRating_DirectDefensiveErrorCanStillDropBelowSix()
    {
        var match = CreateMatch(homeScore: 0, awayScore: 1);
        match.AwayStats.TotalShots = 4;
        match.AwayStats.ShotsOnTarget = 2;
        match.AwayStats.ExpectedGoals = 0.7;

        var defender = new PlayerMatchPerformance
        {
            PlayerName = "At Fault Defender",
            TeamName = match.HomeTeam.Name,
            Position = Position.Defender,
            Rating = 5.5,
            ErrorsLeadingToGoal = 1,
            PenaltiesConceded = 1
        };

        var rating = PlayerMatchRatingService.CalculateContextualRating(match, defender);

        Assert.True(rating < 6.0);
    }

    [Fact]
    public void CalculateContextualRating_DefendersGainMoreThanAttackersFromDefensiveActions()
    {
        var match = CreateMatch(homeScore: 0, awayScore: 0);
        var defender = new PlayerMatchPerformance
        {
            PlayerName = "Defensive Specialist",
            TeamName = match.HomeTeam.Name,
            Position = Position.Defender,
            Rating = 6.0,
            Tackles = 3,
            Interceptions = 2,
            Clearances = 2
        };
        var forward = new PlayerMatchPerformance
        {
            PlayerName = "Tracking Forward",
            TeamName = match.HomeTeam.Name,
            Position = Position.Forward,
            Rating = 6.0,
            Tackles = defender.Tackles,
            Interceptions = defender.Interceptions,
            Clearances = defender.Clearances
        };

        var defenderRating = PlayerMatchRatingService.CalculateContextualRating(match, defender);
        var forwardRating = PlayerMatchRatingService.CalculateContextualRating(match, forward);

        Assert.True(defenderRating > forwardRating);
    }

    [Fact]
    public void CalculateContextualRating_PenalizesDefendersForHeavyConcessions()
    {
        var match = CreateMatch(homeScore: 0, awayScore: 4);
        var defender = new PlayerMatchPerformance
        {
            PlayerName = "Busy Defender",
            TeamName = match.HomeTeam.Name,
            Position = Position.Defender,
            Rating = 8.4,
            Tackles = 5,
            Interceptions = 4,
            Blocks = 2,
            Clearances = 7
        };

        var rating = PlayerMatchRatingService.CalculateContextualRating(match, defender);

        Assert.InRange(rating, 1.0, 6.8);
    }

    [Fact]
    public void CalculateContextualRating_CapsGoalkeeperDespiteManySavesWhenManyGoalsConceded()
    {
        var match = CreateMatch(homeScore: 0, awayScore: 5);
        var goalkeeper = new PlayerMatchPerformance
        {
            PlayerName = "Shot Stopper",
            TeamName = match.HomeTeam.Name,
            Position = Position.Goalkeeper,
            Rating = 9.4,
            Saves = 12
        };

        var rating = PlayerMatchRatingService.CalculateContextualRating(match, goalkeeper);

        Assert.InRange(rating, 1.0, 5.6);
    }

    [Fact]
    public void CalculateContextualRating_RewardsGoalkeeperCleanSheet()
    {
        var match = CreateMatch(homeScore: 1, awayScore: 0);
        var goalkeeper = new PlayerMatchPerformance
        {
            PlayerName = "Clean Sheet Keeper",
            TeamName = match.HomeTeam.Name,
            Position = Position.Goalkeeper,
            Rating = 6.0,
            Saves = 4
        };

        var rating = PlayerMatchRatingService.CalculateContextualRating(match, goalkeeper);

        Assert.True(rating > 6.0);
    }

    private static Match CreateMatch(int homeScore, int awayScore)
    {
        return new Match
        {
            HomeTeam = new Team { Name = "Home" },
            AwayTeam = new Team { Name = "Away" },
            HomeScore = homeScore,
            AwayScore = awayScore
        };
    }
}
