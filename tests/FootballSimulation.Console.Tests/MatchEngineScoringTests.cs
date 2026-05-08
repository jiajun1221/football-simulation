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
        var (secondHomeTeam, secondAwayTeam) = seedDataService.CreateDemoTeams();
        var engine = new MatchEngine();

        var firstResult = engine.SimulateMatch(homeTeam, awayTeam, seed: 99);
        var secondResult = engine.SimulateMatch(secondHomeTeam, secondAwayTeam, seed: 99);

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

    [Fact]
    public void SimulateMatch_DoesNotCreateConsecutiveAttackEventsForSameTeam()
    {
        var seedDataService = new SeedDataService();
        var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
        var engine = new MatchEngine();

        var result = engine.SimulateMatch(homeTeam, awayTeam, seed: 42);

        for (var index = 1; index < result.Events.Count; index++)
        {
            var previousEvent = result.Events[index - 1];
            var currentEvent = result.Events[index];
            if (previousEvent.EventType != EventType.Attack || currentEvent.EventType != EventType.Attack)
            {
                continue;
            }

            var previousTeam = FindEventTeamName(previousEvent, result);
            var currentTeam = FindEventTeamName(currentEvent, result);

            Assert.False(
                !string.IsNullOrWhiteSpace(previousTeam) &&
                string.Equals(previousTeam, currentTeam, StringComparison.OrdinalIgnoreCase),
                $"Consecutive ATTACK events found for {previousTeam} at {previousEvent.Minute}' and {currentEvent.Minute}'.");
        }
    }

    [Fact]
    public void SimulateMatch_FirstOpenPlayEventBelongsToKickoffTeam()
    {
        var seedDataService = new SeedDataService();
        var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
        var engine = new MatchEngine();

        var result = engine.SimulateMatch(homeTeam, awayTeam, seed: 42);
        var firstOpenPlayEvent = result.Events.FirstOrDefault(IsPossessionFlowEvent);

        Assert.NotNull(firstOpenPlayEvent);
        Assert.Equal(homeTeam.Name, FindEventTeamName(firstOpenPlayEvent, result));
    }

    [Fact]
    public void SimulateMatch_FoulingTeamDoesNotImmediatelyAttackAfterFoul()
    {
        var seedDataService = new SeedDataService();
        var engine = new MatchEngine();

        for (var seed = 1; seed <= 20; seed++)
        {
            var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
            var result = engine.SimulateMatch(homeTeam, awayTeam, seed: seed);

            for (var index = 0; index < result.Events.Count - 1; index++)
            {
                var currentEvent = result.Events[index];
                if (currentEvent.EventType != EventType.Foul)
                {
                    continue;
                }

                var foulingTeam = FindEventTeamName(currentEvent, result);
                var nextEvent = result.Events[index + 1];
                if (nextEvent.EventType != EventType.Attack)
                {
                    continue;
                }

                Assert.NotEqual(foulingTeam, FindEventTeamName(nextEvent, result));
            }
        }
    }

    [Fact]
    public void SimulateMatch_DoesNotCreateConsecutiveTurnoverEvents()
    {
        var seedDataService = new SeedDataService();
        var engine = new MatchEngine();

        for (var seed = 1; seed <= 40; seed++)
        {
            var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
            var result = engine.SimulateMatch(homeTeam, awayTeam, seed: seed);

            for (var index = 1; index < result.Events.Count; index++)
            {
                Assert.False(
                    result.Events[index - 1].EventType == EventType.Turnover &&
                    result.Events[index].EventType == EventType.Turnover,
                    $"Consecutive TURNOVER events found at {result.Events[index - 1].Minute}' and {result.Events[index].Minute}' for seed {seed}.");
            }
        }
    }

    [Fact]
    public void SimulateMatch_TurnoverEventsHaveCauseAndCleanPossessionText()
    {
        var seedDataService = new SeedDataService();
        var engine = new MatchEngine();

        for (var seed = 1; seed <= 40; seed++)
        {
            var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
            var result = engine.SimulateMatch(homeTeam, awayTeam, seed: seed);
            var events = result.Events;

            for (var index = 1; index < events.Count; index++)
            {
                if (events[index].EventType != EventType.Turnover)
                {
                    continue;
                }

                var causeEvent = events[index - 1];
                Assert.True(
                    IsTurnoverCauseEvent(causeEvent),
                    $"TURNOVER at {events[index].Minute}' for seed {seed} was not preceded by a cause event.");

                Assert.DoesNotContain("bad pass", events[index].Description, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("miscontrol", events[index].Description, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("dispossessed", events[index].Description, StringComparison.OrdinalIgnoreCase);

                var turnoverTeam = FindEventTeamName(events[index], result);
                Assert.False(string.IsNullOrWhiteSpace(turnoverTeam));
                Assert.False(string.IsNullOrWhiteSpace(events[index].PrimaryPlayerName));
                Assert.Contains(events[index].PrimaryPlayerName!, events[index].Description, StringComparison.OrdinalIgnoreCase);

                var nextPossessionEvent = events
                    .Skip(index + 1)
                    .FirstOrDefault(matchEvent => matchEvent.EventType is not EventType.Halftime and not EventType.Fulltime);

                if (nextPossessionEvent is not null)
                {
                    Assert.Equal(EventType.Attack, nextPossessionEvent.EventType);
                    var nextEventTeam = FindEventTeamName(nextPossessionEvent, result);
                    Assert.True(
                        string.Equals(turnoverTeam, nextEventTeam, StringComparison.OrdinalIgnoreCase),
                        $"Seed {seed}: turnover '{events[index].Description}' should be followed by new possession team, but next event was '{nextPossessionEvent.Description}'.");
                }
            }
        }
    }

    [Fact]
    public void SimulateMatch_DefensiveTurnoverCauseCreditsDefender()
    {
        var seedDataService = new SeedDataService();
        var engine = new MatchEngine();

        for (var seed = 1; seed <= 40; seed++)
        {
            var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
            var result = engine.SimulateMatch(homeTeam, awayTeam, seed: seed);

            foreach (var matchEvent in result.Events.Where(IsDefensiveTurnoverCauseEvent))
            {
                var performance = result.PlayerPerformances.Single(playerPerformance =>
                    playerPerformance.PlayerName == matchEvent.PrimaryPlayerName);

                if (matchEvent.EventType == EventType.Tackle)
                {
                    Assert.True(performance.Tackles > 0);
                }

                if (matchEvent.EventType == EventType.Interception)
                {
                    Assert.True(performance.Interceptions > 0);
                }
            }
        }
    }

    [Fact]
    public void SimulateMatch_PenaltyResultHasDecisionAndTakerBuildUp()
    {
        var seedDataService = new SeedDataService();
        var engine = new MatchEngine();

        for (var seed = 1; seed <= 40; seed++)
        {
            var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
            var result = engine.SimulateMatch(homeTeam, awayTeam, seed: seed);
            var events = result.Events;

            for (var index = 0; index < events.Count; index++)
            {
                if (events[index].EventType != EventType.Penalty)
                {
                    continue;
                }

                Assert.True(index >= 2, "Penalty result must be preceded by decision and taker events.");
                Assert.Equal(EventType.PenaltyDecision, events[index - 2].EventType);
                Assert.Equal(EventType.PenaltyTaker, events[index - 1].EventType);
                Assert.Equal(events[index].Minute, events[index - 2].Minute);
                Assert.Equal(events[index].Minute, events[index - 1].Minute);
            }
        }
    }

    [Fact]
    public void SimulateMatch_WonderGoalOnlyComesAfterShot()
    {
        var seedDataService = new SeedDataService();
        var engine = new MatchEngine();

        for (var seed = 1; seed <= 80; seed++)
        {
            var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
            var result = engine.SimulateMatch(homeTeam, awayTeam, seed: seed);
            var events = result.Events;

            for (var index = 1; index < events.Count; index++)
            {
                if (events[index].EventType != EventType.WonderGoal)
                {
                    continue;
                }

                Assert.Equal(EventType.Shot, events[index - 1].EventType);
                Assert.Equal(FindEventTeamName(events[index - 1], result), FindEventTeamName(events[index], result));
                if (index >= 2)
                {
                    Assert.True(events[index - 2].EventType is not EventType.Offside and not EventType.Turnover);
                }
            }
        }
    }

    [Fact]
    public void SimulateMatch_OffsideGivesOpponentNextAttack()
    {
        var seedDataService = new SeedDataService();
        var engine = new MatchEngine();

        for (var seed = 1; seed <= 80; seed++)
        {
            var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
            var result = engine.SimulateMatch(homeTeam, awayTeam, seed: seed);
            var events = result.Events;

            for (var index = 0; index < events.Count - 1; index++)
            {
                if (events[index].EventType != EventType.Offside)
                {
                    continue;
                }

                var offsideTeam = FindEventTeamName(events[index], result);
                var expectedRestartTeam = offsideTeam == homeTeam.Name ? awayTeam.Name : homeTeam.Name;
                var nextOpenPlayEvent = events
                    .Skip(index + 1)
                    .FirstOrDefault(matchEvent => matchEvent.EventType is not EventType.Halftime and not EventType.Fulltime);

                if (nextOpenPlayEvent is null)
                {
                    continue;
                }

                Assert.Equal(EventType.Attack, nextOpenPlayEvent.EventType);
                var actualRestartTeam = FindEventTeamName(nextOpenPlayEvent, result);
                Assert.True(
                    string.Equals(expectedRestartTeam, actualRestartTeam, StringComparison.OrdinalIgnoreCase),
                    $"Seed {seed}: offside '{events[index].Description}' should restart with {expectedRestartTeam}, but next event was '{nextOpenPlayEvent.Description}'.");
                var restartDescriptions = new[]
                {
                    $"{expectedRestartTeam} restart play.",
                    $"{expectedRestartTeam} build from the back.",
                    $"{expectedRestartTeam} regain possession.",
                    $"{expectedRestartTeam} look to settle on the ball."
                };
                Assert.True(restartDescriptions.Contains(nextOpenPlayEvent.Description));
            }
        }
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

    private static bool IsPossessionFlowEvent(MatchEvent matchEvent)
    {
        return matchEvent.EventType is EventType.Attack
            or EventType.Turnover
            or EventType.DefensiveStop
            or EventType.Shot
            or EventType.Save
            or EventType.Goal
            or EventType.Miss
            or EventType.Offside;
    }

    private static bool IsTurnoverCauseEvent(MatchEvent matchEvent)
    {
        return matchEvent.EventType is EventType.BadPass
            or EventType.Miscontrol
            or EventType.Tackle
            or EventType.Interception
            or EventType.Pressure
            or EventType.BlockedPass;
    }

    private static bool IsDefensiveTurnoverCauseEvent(MatchEvent matchEvent)
    {
        return matchEvent.EventType is EventType.Tackle
            or EventType.Interception
            or EventType.Pressure
            or EventType.BlockedPass;
    }

    private static string? FindEventTeamName(MatchEvent matchEvent, Match match)
    {
        if (EventMentionsTeam(matchEvent.Description, match.HomeTeam.Name))
        {
            return match.HomeTeam.Name;
        }

        if (EventMentionsTeam(matchEvent.Description, match.AwayTeam.Name))
        {
            return match.AwayTeam.Name;
        }

        return null;
    }

    private static bool EventMentionsTeam(string description, string teamName)
    {
        return description.StartsWith($"{teamName} ", StringComparison.OrdinalIgnoreCase) ||
            description.Contains($" for {teamName}", StringComparison.OrdinalIgnoreCase) ||
            description.Contains($" from {teamName}", StringComparison.OrdinalIgnoreCase) ||
            description.Contains($" by {teamName}", StringComparison.OrdinalIgnoreCase);
    }
}
