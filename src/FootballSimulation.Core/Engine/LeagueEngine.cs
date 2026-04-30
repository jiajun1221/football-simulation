using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Engine;

public class LeagueEngine
{
    private readonly MatchEngine _matchEngine;
    private readonly LeagueScheduleService _scheduleService;
    private readonly LeagueTableService _tableService;

    public LeagueEngine()
        : this(new MatchEngine(), new LeagueScheduleService(), new LeagueTableService())
    {
    }

    public LeagueEngine(
        MatchEngine matchEngine,
        LeagueScheduleService scheduleService,
        LeagueTableService tableService)
    {
        _matchEngine = matchEngine;
        _scheduleService = scheduleService;
        _tableService = tableService;
    }

    public League SimulateLeague(string leagueName, List<Team> teams, int? seed = null)
    {
        var league = CreateLeague(leagueName, teams);

        foreach (var fixture in league.Fixtures.OrderBy(fixture => fixture.RoundNumber))
        {
            SimulateFixture(league, fixture, seed);
        }

        return league;
    }

    public League CreateLeague(string leagueName, List<Team> teams)
    {
        if (teams.Count < 2)
        {
            throw new ArgumentException("A league needs at least two teams.", nameof(teams));
        }

        return new League
        {
            Name = leagueName,
            Teams = teams,
            Fixtures = _scheduleService.GenerateFixtures(teams),
            Table = _tableService.CreateTable(teams)
        };
    }

    public Match SimulateFixture(League league, Fixture fixture, int? seed = null, MatchSimulationOptions? options = null)
    {
        if (fixture.IsPlayed)
        {
            throw new InvalidOperationException("This fixture has already been played.");
        }

        var fixtureIndex = league.Fixtures.IndexOf(fixture);
        if (fixtureIndex < 0)
        {
            throw new ArgumentException("The fixture does not belong to this league.", nameof(fixture));
        }

        int? matchSeed = seed.HasValue
            ? seed.Value + fixture.RoundNumber * 100 + fixtureIndex
            : null;
        var result = _matchEngine.SimulateMatch(fixture.HomeTeam, fixture.AwayTeam, matchSeed, options: CreateOptions(options));

        fixture.Result = result;
        fixture.IsPlayed = true;

        _tableService.ApplyMatchResult(league.Table, result);
        league.Table = _tableService.SortTable(league.Table);

        return result;
    }

    public Match SimulateFixtureFirstHalf(League league, Fixture fixture, int? seed = null, MatchSimulationOptions? options = null)
    {
        ValidateFixtureIsPlayable(league, fixture);

        var fixtureIndex = league.Fixtures.IndexOf(fixture);
        int? matchSeed = seed.HasValue
            ? seed.Value + fixture.RoundNumber * 100 + fixtureIndex
            : null;

        return _matchEngine.SimulateFirstHalf(fixture.HomeTeam, fixture.AwayTeam, matchSeed, CreateOptions(options));
    }

    public Match CreateLiveFixtureMatch(League league, Fixture fixture, MatchSimulationOptions? options = null)
    {
        ValidateFixtureIsPlayable(league, fixture);
        return _matchEngine.CreateLiveMatch(fixture.HomeTeam, fixture.AwayTeam, CreateOptions(options));
    }

    public Match AdvanceLiveFixture(
        League league,
        Fixture fixture,
        Match match,
        int startMinute,
        int endMinute,
        bool includeFulltime,
        int? seed = null,
        MatchSimulationOptions? options = null)
    {
        ValidateFixtureIsPlayable(league, fixture);

        var fixtureIndex = league.Fixtures.IndexOf(fixture);
        int? matchSeed = seed.HasValue
            ? seed.Value + fixture.RoundNumber * 100 + fixtureIndex
            : null;

        return _matchEngine.AdvanceMatch(match, startMinute, endMinute, includeFulltime, matchSeed, CreateOptions(options));
    }

    public Match SimulateFixtureSecondHalf(League league, Fixture fixture, Match match, int? seed = null, MatchSimulationOptions? options = null)
    {
        ValidateFixtureIsPlayable(league, fixture);

        var fixtureIndex = league.Fixtures.IndexOf(fixture);
        int? matchSeed = seed.HasValue
            ? seed.Value + fixture.RoundNumber * 100 + fixtureIndex
            : null;
        var result = _matchEngine.SimulateSecondHalf(match, matchSeed, CreateOptions(options));

        fixture.Result = result;
        fixture.IsPlayed = true;

        _tableService.ApplyMatchResult(league.Table, result);
        league.Table = _tableService.SortTable(league.Table);

        return result;
    }

    public void CompleteLiveFixture(League league, Fixture fixture, Match match)
    {
        ValidateFixtureIsPlayable(league, fixture);

        fixture.Result = match;
        fixture.IsPlayed = true;

        _tableService.ApplyMatchResult(league.Table, match);
        league.Table = _tableService.SortTable(league.Table);
    }

    public List<Match> SimulateRemainingFixturesInRound(League league, int roundNumber, int? seed = null, MatchSimulationOptions? options = null)
    {
        var results = new List<Match>();
        var roundFixtures = league.Fixtures
            .Where(fixture => fixture.RoundNumber == roundNumber && !fixture.IsPlayed)
            .ToList();

        foreach (var fixture in roundFixtures)
        {
            results.Add(SimulateFixture(league, fixture, seed, options));
        }

        return results;
    }

    private static void ValidateFixtureIsPlayable(League league, Fixture fixture)
    {
        if (fixture.IsPlayed)
        {
            throw new InvalidOperationException("This fixture has already been played.");
        }

        if (!league.Fixtures.Contains(fixture))
        {
            throw new ArgumentException("The fixture does not belong to this league.", nameof(fixture));
        }
    }

    private static MatchSimulationOptions CreateOptions(MatchSimulationOptions? options)
    {
        return options ?? new MatchSimulationOptions();
    }
}
