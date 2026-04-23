using FootballSimulation.Data;
using FootballSimulation.Engine;
using FootballSimulation.Models;

namespace FootballSimulation.Console.Tests;

public class MatchEngineScoringTests
{
    [Fact]
    public void SimulateMatch_WithSameSeed_ReturnsSameScoreAndEventLog()
    {
        var seedDataService = new SeedDataService();
        var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
        var engine = new MatchEngine();

        var firstResult = engine.SimulateMatch(homeTeam, awayTeam, seed: 99);
        var secondResult = engine.SimulateMatch(homeTeam, awayTeam, seed: 99);

        Assert.Equal(firstResult.HomeScore, secondResult.HomeScore);
        Assert.Equal(firstResult.AwayScore, secondResult.AwayScore);
        Assert.Equal(firstResult.Events.Select(FormatEvent), secondResult.Events.Select(FormatEvent));
    }

    [Fact]
    public void SimulateMatch_GoalEventsMatchFinalScore()
    {
        var seedDataService = new SeedDataService();
        var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
        var engine = new MatchEngine();

        var result = engine.SimulateMatch(homeTeam, awayTeam, seed: 42);
        var goalEvents = result.Events.Where(IsScoringEvent).ToList();

        Assert.Equal(result.HomeScore + result.AwayScore, goalEvents.Count);
        Assert.All(goalEvents, matchEvent => Assert.False(string.IsNullOrWhiteSpace(matchEvent.PrimaryPlayerName)));
    }

    [Fact]
    public void SimulateMatch_StillReturnsFullNinetyMinuteMatch()
    {
        var seedDataService = new SeedDataService();
        var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
        var engine = new MatchEngine();

        var result = engine.SimulateMatch(homeTeam, awayTeam, seed: 42);

        Assert.Equal(90, result.CurrentMinute);
        Assert.Equal(MatchPhase.Fulltime, result.CurrentPhase);
        Assert.Contains(result.Events, matchEvent => matchEvent.EventType == EventType.Fulltime && matchEvent.Minute == 90);
    }

    [Fact]
    public void SimulateFirstHalf_StopsAtHalftime()
    {
        var seedDataService = new SeedDataService();
        var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
        var engine = new MatchEngine();

        var result = engine.SimulateFirstHalf(homeTeam, awayTeam, seed: 42);

        Assert.Equal(45, result.CurrentMinute);
        Assert.Equal(MatchPhase.Halftime, result.CurrentPhase);
        Assert.DoesNotContain(result.Events, matchEvent => matchEvent.Minute > 45);
        Assert.Contains(result.Events, matchEvent => matchEvent.EventType == EventType.Halftime);
    }

    [Fact]
    public void SimulateSecondHalf_AppendsEventsAndPreservesFirstHalfScore()
    {
        var seedDataService = new SeedDataService();
        var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
        var engine = new MatchEngine();
        var firstHalf = engine.SimulateFirstHalf(homeTeam, awayTeam, seed: 42);
        var firstHalfHomeScore = firstHalf.HomeScore;
        var firstHalfAwayScore = firstHalf.AwayScore;
        var firstHalfEventCount = firstHalf.Events.Count;

        homeTeam.Formation = "3-5-2";
        var fullMatch = engine.SimulateSecondHalf(firstHalf, seed: 142);

        Assert.True(fullMatch.Events.Count > firstHalfEventCount);
        Assert.Contains(fullMatch.Events, matchEvent => matchEvent.Minute > 45);
        Assert.Contains(fullMatch.Events, matchEvent => matchEvent.EventType == EventType.Fulltime && matchEvent.Minute == 90);
        Assert.True(fullMatch.HomeScore >= firstHalfHomeScore);
        Assert.True(fullMatch.AwayScore >= firstHalfAwayScore);
    }

    private static string FormatEvent(MatchEvent matchEvent)
    {
        return $"{matchEvent.Minute}|{matchEvent.EventType}|{matchEvent.Description}";
    }

    private static bool IsScoringEvent(MatchEvent matchEvent)
    {
        return matchEvent.EventType == EventType.Goal
            || matchEvent.EventType == EventType.WonderGoal
            || (matchEvent.EventType == EventType.Penalty
                && matchEvent.Description.Contains("scores", StringComparison.OrdinalIgnoreCase));
    }
}
