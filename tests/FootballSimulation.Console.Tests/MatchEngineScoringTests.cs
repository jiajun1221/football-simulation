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
        var ruledOutGoalEvents = result.Events.Count(IsGoalDisallowedEvent);

        Assert.Equal(result.HomeScore + result.AwayScore, goalEvents.Count - ruledOutGoalEvents);
        Assert.All(goalEvents, matchEvent => Assert.False(string.IsNullOrWhiteSpace(matchEvent.PrimaryPlayerName)));
    }

    [Fact]
    public void SimulateMatch_StillReturnsFullNinetyMinuteMatch()
    {
        var seedDataService = new SeedDataService();
        var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
        var engine = new MatchEngine();

        var result = engine.SimulateMatch(homeTeam, awayTeam, seed: 42);

        var expectedFulltimeMinute = 90 + result.FirstHalfAddedMinutes + result.SecondHalfAddedMinutes;

        Assert.Equal(expectedFulltimeMinute, result.CurrentMinute);
        Assert.Equal(MatchPhase.Fulltime, result.CurrentPhase);
        Assert.Contains(result.Events, matchEvent => matchEvent.EventType == EventType.Fulltime && matchEvent.Minute == expectedFulltimeMinute);
    }

    [Fact]
    public void SimulateFirstHalf_StopsAtHalftime()
    {
        var seedDataService = new SeedDataService();
        var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
        var engine = new MatchEngine();

        var result = engine.SimulateFirstHalf(homeTeam, awayTeam, seed: 42);

        var expectedHalftimeMinute = 45 + result.FirstHalfAddedMinutes;

        Assert.Equal(expectedHalftimeMinute, result.CurrentMinute);
        Assert.Equal(MatchPhase.Halftime, result.CurrentPhase);
        Assert.DoesNotContain(result.Events, matchEvent => matchEvent.Minute > expectedHalftimeMinute);
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
        var expectedFulltimeMinute = 90 + fullMatch.FirstHalfAddedMinutes + fullMatch.SecondHalfAddedMinutes;
        Assert.Contains(fullMatch.Events, matchEvent => matchEvent.EventType == EventType.Fulltime && matchEvent.Minute == expectedFulltimeMinute);
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
                    .FirstOrDefault(matchEvent => matchEvent.EventType is EventType.Attack or EventType.Kickoff);

                if (nextPossessionEvent is not null && nextPossessionEvent.EventType != EventType.Kickoff)
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
    public void MatchDramaService_DoesNotCreateFreeFloatingGoalkeeperHeroics()
    {
        var seedDataService = new SeedDataService();
        var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
        foreach (var player in homeTeam.Players.Concat(awayTeam.Players))
        {
            player.Stamina = 100;
            player.CurrentStamina = 100;
            player.MatchesPlayedRecently = 0;
            player.Traits.Clear();
        }

        var service = new MatchDramaService();
        var result = service.TryCreateDramaEvent(new MatchEventContext
        {
            Match = new Match { HomeTeam = homeTeam, AwayTeam = awayTeam },
            HomeTeam = homeTeam,
            AwayTeam = awayTeam,
            Random = new SequenceRandom(0.0, 0.99, 0.10),
            Minute = 67,
            WeatherCondition = WeatherCondition.Clear,
            IsRivalryMatch = false,
            HomeAttackStrength = 80,
            AwayAttackStrength = 80,
            HomeDefenseStrength = 80,
            AwayDefenseStrength = 80
        });

        Assert.NotNull(result);
        Assert.Equal(EventType.CrowdMomentum, result.EventType);
    }

    [Fact]
    public void SimulateMatch_LimitsCrowdMomentumSpam()
    {
        var seedDataService = new SeedDataService();
        var engine = new MatchEngine();

        for (var seed = 1; seed <= 140; seed++)
        {
            var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
            var result = engine.SimulateMatch(homeTeam, awayTeam, seed: seed);
            var crowdEvents = result.Events
                .Select((matchEvent, index) => new { MatchEvent = matchEvent, Index = index })
                .Where(item => item.MatchEvent.EventType == EventType.CrowdMomentum)
                .ToList();

            Assert.True(crowdEvents.Count <= 3, $"Seed {seed}: expected at most 3 crowd momentum events.");

            for (var index = 0; index < crowdEvents.Count; index++)
            {
                if (index > 0)
                {
                    Assert.True(
                        crowdEvents[index].MatchEvent.Minute - crowdEvents[index - 1].MatchEvent.Minute >= 10,
                        $"Seed {seed}: crowd momentum events were too close together.");
                }

                if (crowdEvents[index].Index > 0)
                {
                    Assert.NotEqual(EventType.CrowdMomentum, result.Events[crowdEvents[index].Index - 1].EventType);
                }
            }

            var countsByTeam = crowdEvents
                .Select(item => FindEventTeamName(item.MatchEvent, result))
                .Where(teamName => !string.IsNullOrWhiteSpace(teamName))
                .GroupBy(teamName => teamName!, StringComparer.OrdinalIgnoreCase);

            foreach (var teamCrowdEvents in countsByTeam)
            {
                Assert.True(teamCrowdEvents.Count() <= 2, $"Seed {seed}: {teamCrowdEvents.Key} had too many crowd momentum events.");
            }
        }
    }

    [Fact]
    public void SimulateFirstHalf_AnnouncesAddedTimeBeforeHalftime()
    {
        var seedDataService = new SeedDataService();
        var engine = new MatchEngine();
        Match? result = null;

        for (var seed = 1; seed <= 40; seed++)
        {
            var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
            result = engine.SimulateFirstHalf(homeTeam, awayTeam, seed: seed);
            if (result.FirstHalfAddedMinutes > 0)
            {
                break;
            }
        }

        Assert.NotNull(result);
        Assert.True(result.FirstHalfAddedTimeAnnounced);
        Assert.True(result.FirstHalfAddedMinutes > 0);

        var addedTime = Assert.Single(result.Events, matchEvent => matchEvent.EventType == EventType.AddedTime);
        var halftime = Assert.Single(result.Events, matchEvent => matchEvent.EventType == EventType.Halftime);

        Assert.Equal(45, addedTime.Minute);
        Assert.Equal(45 + result.FirstHalfAddedMinutes, halftime.Minute);
        Assert.Equal($"45+{result.FirstHalfAddedMinutes}'", halftime.DisplayMinuteText);
    }

    [Fact]
    public void SimulateMatch_SecondHalfCanFinishInAddedTime()
    {
        var seedDataService = new SeedDataService();
        var engine = new MatchEngine();
        Match? result = null;

        for (var seed = 1; seed <= 40; seed++)
        {
            var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
            result = engine.SimulateMatch(homeTeam, awayTeam, seed: seed);
            if (result.SecondHalfAddedMinutes > 0)
            {
                break;
            }
        }

        Assert.NotNull(result);
        Assert.True(result.SecondHalfAddedTimeAnnounced);
        Assert.True(result.SecondHalfAddedMinutes > 0);

        var fulltime = Assert.Single(result.Events, matchEvent => matchEvent.EventType == EventType.Fulltime);
        var expectedFulltimeMinute = 90 + result.FirstHalfAddedMinutes + result.SecondHalfAddedMinutes;

        Assert.Equal(expectedFulltimeMinute, fulltime.Minute);
        Assert.Equal($"90+{result.SecondHalfAddedMinutes}'", fulltime.DisplayMinuteText);
    }

    [Fact]
    public void FormatDisplayMinute_UsesAddedTimeNotation()
    {
        var match = new Match
        {
            FirstHalfAddedMinutes = 3,
            SecondHalfAddedMinutes = 5
        };

        Assert.Equal("45'", MatchEngine.FormatDisplayMinute(match, 45));
        Assert.Equal("45+2'", MatchEngine.FormatDisplayMinute(match, 47));
        Assert.Equal("46'", MatchEngine.FormatDisplayMinute(match, 49));
        Assert.Equal("90'", MatchEngine.FormatDisplayMinute(match, 93));
        Assert.Equal("90+4'", MatchEngine.FormatDisplayMinute(match, 97));
    }

    [Fact]
    public void SimulateMatch_ConfrontationsOnlyFollowStoppedPlayTriggers()
    {
        var seedDataService = new SeedDataService();
        var engine = new MatchEngine();

        for (var seed = 1; seed <= 80; seed++)
        {
            var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
            var result = engine.SimulateMatch(homeTeam, awayTeam, seed: seed);
            var events = result.Events;
            var confrontationEvents = events
                .Where(matchEvent => matchEvent.EventType == EventType.Confrontation)
                .ToList();
            Assert.True(
                confrontationEvents.Count <= 2,
                $"Seed {seed}: expected at most 2 confrontations, found {confrontationEvents.Count}.");

            for (var confrontationIndex = 1; confrontationIndex < confrontationEvents.Count; confrontationIndex++)
            {
                Assert.True(
                    confrontationEvents[confrontationIndex - 1].Minute + 20 <= confrontationEvents[confrontationIndex].Minute,
                    $"Seed {seed}: confrontations were too close together.");
            }

            for (var index = 0; index < events.Count; index++)
            {
                if (events[index].EventType != EventType.Confrontation)
                {
                    continue;
                }

                var previousEvent = index > 0 ? events[index - 1] : null;
                Assert.NotNull(previousEvent);
                Assert.True(
                    IsConfrontationTriggerEvent(previousEvent!.EventType),
                    $"Seed {seed}: confrontation at {events[index].Minute}' followed {previousEvent.EventType}: {previousEvent.Description}");
            }
        }
    }

    [Fact]
    public void SimulateMatch_StraightRedDoesNotAlsoShowYellowForSameIncident()
    {
        var seedDataService = new SeedDataService();
        var engine = new MatchEngine();

        for (var seed = 1; seed <= 120; seed++)
        {
            var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
            var result = engine.SimulateMatch(homeTeam, awayTeam, seed: seed);
            var events = result.Events;

            for (var index = 0; index < events.Count; index++)
            {
                var redCard = events[index];
                if (redCard.EventType != EventType.RedCard ||
                    redCard.Description.Contains("Second yellow", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var sameIncidentYellow = events
                    .Take(index)
                    .Any(matchEvent =>
                        matchEvent.Minute == redCard.Minute &&
                        matchEvent.EventType == EventType.YellowCard &&
                        string.Equals(matchEvent.PrimaryPlayerName, redCard.PrimaryPlayerName, StringComparison.OrdinalIgnoreCase));

                Assert.False(
                    sameIncidentYellow,
                    $"Seed {seed}: {redCard.PrimaryPlayerName} received yellow and straight red in the same incident.");
            }
        }
    }

    [Fact]
    public void SimulateMatch_GoalkeeperHeroicsDoesNotDuplicateSaveFeed()
    {
        var seedDataService = new SeedDataService();
        var engine = new MatchEngine();

        for (var seed = 1; seed <= 120; seed++)
        {
            var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
            var result = engine.SimulateMatch(homeTeam, awayTeam, seed: seed);
            var events = result.Events;

            for (var index = 1; index < events.Count; index++)
            {
                if (events[index].EventType != EventType.GoalkeeperHeroics)
                {
                    continue;
                }

                var keeperName = events[index].PrimaryPlayerName;
                var keeperTeam = FindPlayerTeamName(keeperName, result);
                var previousEvent = events[index - 1];
                var duplicatesPreviousSave =
                    previousEvent.EventType == EventType.Save &&
                    previousEvent.Minute == events[index].Minute &&
                    string.Equals(previousEvent.SecondaryPlayerName, keeperName, StringComparison.OrdinalIgnoreCase);

                Assert.Contains($"keeps {keeperTeam} alive", events[index].Description, StringComparison.OrdinalIgnoreCase);
                Assert.False(
                    duplicatesPreviousSave,
                    $"Seed {seed}: goalkeeper heroics duplicated the previous save for {keeperName}.");
            }
        }
    }

    [Fact]
    public void SimulateMatch_DoesNotGenerateDefenseThenMissForSameShot()
    {
        var seedDataService = new SeedDataService();
        var engine = new MatchEngine();

        for (var seed = 1; seed <= 140; seed++)
        {
            var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
            var result = engine.SimulateMatch(homeTeam, awayTeam, seed: seed);
            var events = result.Events;

            for (var shotIndex = 0; shotIndex < events.Count; shotIndex++)
            {
                if (events[shotIndex].EventType != EventType.Shot)
                {
                    continue;
                }

                var sequence = events
                    .Skip(shotIndex + 1)
                    .TakeWhile(matchEvent => matchEvent.EventType != EventType.Attack && matchEvent.EventType != EventType.Shot)
                    .ToList();

                for (var index = 1; index < sequence.Count; index++)
                {
                    var defenseThenMiss = IsShotBlockEvent(sequence[index - 1]) && sequence[index].EventType == EventType.Miss;
                    Assert.False(
                        defenseThenMiss,
                        $"Seed {seed}: shot sequence generated defense/block and then miss for the same shot.");
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
                if (events[index].EventType != EventType.Offside || IsGoalDisallowedEvent(events[index]))
                {
                    continue;
                }

                var offsideTeam = FindEventTeamName(events[index], result);
                var expectedRestartTeam = offsideTeam == homeTeam.Name ? awayTeam.Name : homeTeam.Name;
                var nextOpenPlayEvent = events
                    .Skip(index + 1)
                    .FirstOrDefault(matchEvent => matchEvent.EventType is EventType.Attack or EventType.Kickoff);

                if (nextOpenPlayEvent is null || nextOpenPlayEvent.EventType == EventType.Kickoff)
                {
                    continue;
                }

                Assert.Equal(EventType.Attack, nextOpenPlayEvent.EventType);
                var actualRestartTeam = FindEventTeamName(nextOpenPlayEvent, result);
                Assert.True(
                    string.Equals(expectedRestartTeam, actualRestartTeam, StringComparison.OrdinalIgnoreCase),
                    $"Seed {seed}: offside '{events[index].Description}' should restart with {expectedRestartTeam}, but next event was '{nextOpenPlayEvent.Description}'.");
            }
        }
    }

    [Fact]
    public void SimulateMatch_NormalOffsideDoesNotTriggerVarReview()
    {
        var seedDataService = new SeedDataService();
        var engine = new MatchEngine();

        for (var seed = 1; seed <= 120; seed++)
        {
            var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
            var result = engine.SimulateMatch(homeTeam, awayTeam, seed: seed);
            var events = result.Events;

            for (var index = 0; index < events.Count - 1; index++)
            {
                if (events[index].EventType != EventType.Offside || IsGoalDisallowedEvent(events[index]))
                {
                    continue;
                }

                Assert.NotEqual(EventType.VarCheck, events[index + 1].EventType);
                Assert.NotEqual(EventType.VarDecision, events[index + 1].EventType);
            }
        }
    }

    [Fact]
    public void SimulateMatch_ChanceCreatedAndShotUseCorrectPlayers()
    {
        var seedDataService = new SeedDataService();
        var engine = new MatchEngine();

        for (var seed = 1; seed <= 100; seed++)
        {
            var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
            var result = engine.SimulateMatch(homeTeam, awayTeam, seed: seed);
            var events = result.Events;

            for (var index = 0; index < events.Count - 1; index++)
            {
                if (events[index].EventType != EventType.ChanceCreated)
                {
                    continue;
                }

                var chanceCreated = events[index];
                var shot = events[index + 1];

                Assert.Equal(EventType.Shot, shot.EventType);
                Assert.Equal(chanceCreated.SecondaryPlayerName ?? chanceCreated.PrimaryPlayerName, shot.PrimaryPlayerName);
                Assert.Equal(chanceCreated.PrimaryPlayerName, shot.SecondaryPlayerName ?? shot.PrimaryPlayerName);
            }
        }
    }

    [Fact]
    public void SimulateMatch_GoalAssistUsesChanceCreatorWhenDifferentFromScorer()
    {
        var seedDataService = new SeedDataService();
        var engine = new MatchEngine();

        for (var seed = 1; seed <= 140; seed++)
        {
            var (homeTeam, awayTeam) = seedDataService.CreateDemoTeams();
            var result = engine.SimulateMatch(homeTeam, awayTeam, seed: seed);
            var events = result.Events;

            for (var index = 1; index < events.Count; index++)
            {
                if (events[index].EventType != EventType.Goal ||
                    events[index].Description.Contains("Own goal", StringComparison.OrdinalIgnoreCase) ||
                    index < 2 ||
                    events[index - 2].EventType != EventType.ChanceCreated ||
                    events[index - 1].EventType != EventType.Shot ||
                    !string.Equals(events[index].PrimaryPlayerName, events[index - 1].PrimaryPlayerName, StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(events[index - 1].SecondaryPlayerName))
                {
                    continue;
                }

                Assert.Equal(events[index - 2].PrimaryPlayerName, events[index - 1].SecondaryPlayerName);
                Assert.Equal(events[index - 1].SecondaryPlayerName, events[index].SecondaryPlayerName);
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

    private static bool IsShotBlockEvent(MatchEvent matchEvent)
    {
        return matchEvent.EventType == EventType.DefensiveStop &&
            (matchEvent.Description.Contains("block", StringComparison.OrdinalIgnoreCase) ||
                matchEvent.Description.Contains("clearance", StringComparison.OrdinalIgnoreCase) ||
                matchEvent.Description.Contains("clears", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsGoalDisallowedEvent(MatchEvent matchEvent)
    {
        return (matchEvent.EventType is EventType.VarDecision or EventType.Offside) &&
            (matchEvent.Description.Contains("goal ruled out", StringComparison.OrdinalIgnoreCase) ||
                matchEvent.Description.Contains("goal is ruled out", StringComparison.OrdinalIgnoreCase) ||
                matchEvent.Description.Contains("goal disallowed", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsConfrontationTriggerEvent(EventType eventType)
    {
        return eventType is EventType.Foul
            or EventType.PenaltyDecision
            or EventType.YellowCard
            or EventType.RedCard
            or EventType.VarCheck
            or EventType.VarDecision
            or EventType.RefereeControversy
            or EventType.Offside
            or EventType.CornerKick
            or EventType.SetPieceDanger
            or EventType.RivalryAtmosphere;
    }

    private static bool IsPossessionFlowEvent(MatchEvent matchEvent)
    {
        return matchEvent.EventType is EventType.Attack
            or EventType.ChanceCreated
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

    private static string? FindPlayerTeamName(string? playerName, Match match)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return null;
        }

        if (match.HomeTeam.Players.Any(player => string.Equals(player.Name, playerName, StringComparison.OrdinalIgnoreCase)))
        {
            return match.HomeTeam.Name;
        }

        if (match.AwayTeam.Players.Any(player => string.Equals(player.Name, playerName, StringComparison.OrdinalIgnoreCase)))
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

    private sealed class SequenceRandom(params double[] values) : Random
    {
        private int _index;

        public override double NextDouble()
        {
            if (values.Length == 0)
            {
                return base.NextDouble();
            }

            var value = _index < values.Length
                ? values[_index]
                : values[^1];
            _index++;
            return value;
        }
    }
}
