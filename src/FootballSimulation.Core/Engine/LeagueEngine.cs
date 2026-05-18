using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Engine;

public class LeagueEngine
{
    private readonly MatchEngine _matchEngine;
    private readonly LeagueScheduleService _scheduleService;
    private readonly LeagueTableService _tableService;
    private readonly PlayerFormStatusService _playerFormStatusService;
    private readonly PlayerSeasonStatsService _playerSeasonStatsService = new();

    public LeagueEngine()
        : this(new MatchEngine(), new LeagueScheduleService(), new LeagueTableService())
    {
    }

    public LeagueEngine(
        MatchEngine matchEngine,
        LeagueScheduleService scheduleService,
        LeagueTableService tableService,
        PlayerFormStatusService? playerFormStatusService = null)
    {
        _matchEngine = matchEngine;
        _scheduleService = scheduleService;
        _tableService = tableService;
        _playerFormStatusService = playerFormStatusService ?? new PlayerFormStatusService();
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
        return CreateLeague(string.Empty, leagueName, string.Empty, teams);
    }

    public League CreateLeague(string leagueId, string leagueName, string season, List<Team> teams)
    {
        if (teams.Count < 2)
        {
            throw new ArgumentException("A league needs at least two teams.", nameof(teams));
        }

        return new League
        {
            LeagueId = leagueId,
            Name = leagueName,
            Season = season,
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
        var simulationOptions = CreateOptions(options);
        PrepareFixtureTeams(fixture, simulationOptions);
        var result = _matchEngine.SimulateMatch(fixture.HomeTeam, fixture.AwayTeam, matchSeed, options: simulationOptions);
        _playerFormStatusService.UpdateMatchPlayerFormStatuses(result);

        fixture.Result = result;
        fixture.IsPlayed = true;

        _tableService.ApplyMatchResult(league.Table, result);
        league.Table = _tableService.SortTable(league.Table);
        _playerSeasonStatsService.RebuildLeagueSeasonStats(league);

        return result;
    }

    public Match SimulateFixtureFirstHalf(League league, Fixture fixture, int? seed = null, MatchSimulationOptions? options = null)
    {
        ValidateFixtureIsPlayable(league, fixture);

        var fixtureIndex = league.Fixtures.IndexOf(fixture);
        int? matchSeed = seed.HasValue
            ? seed.Value + fixture.RoundNumber * 100 + fixtureIndex
            : null;

        var simulationOptions = CreateOptions(options);
        PrepareFixtureTeams(fixture, simulationOptions);
        return _matchEngine.SimulateFirstHalf(fixture.HomeTeam, fixture.AwayTeam, matchSeed, simulationOptions);
    }

    public Match CreateLiveFixtureMatch(League league, Fixture fixture, MatchSimulationOptions? options = null)
    {
        ValidateFixtureIsPlayable(league, fixture);
        var simulationOptions = CreateOptions(options);
        PrepareFixtureTeams(fixture, simulationOptions);
        return _matchEngine.CreateLiveMatch(fixture.HomeTeam, fixture.AwayTeam, simulationOptions);
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
        _playerFormStatusService.UpdateMatchPlayerFormStatuses(result);

        fixture.Result = result;
        fixture.IsPlayed = true;

        _tableService.ApplyMatchResult(league.Table, result);
        league.Table = _tableService.SortTable(league.Table);
        _playerSeasonStatsService.RebuildLeagueSeasonStats(league);

        return result;
    }

    public void CompleteLiveFixture(League league, Fixture fixture, Match match)
    {
        ValidateFixtureIsPlayable(league, fixture);

        fixture.Result = match;
        fixture.IsPlayed = true;
        _playerFormStatusService.UpdateMatchPlayerFormStatuses(match);

        _tableService.ApplyMatchResult(league.Table, match);
        league.Table = _tableService.SortTable(league.Table);
        _playerSeasonStatsService.RebuildLeagueSeasonStats(league);
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

    private static void PrepareFixtureTeams(Fixture fixture, MatchSimulationOptions options)
    {
        if (!IsHumanControlled(fixture.HomeTeam, options))
        {
            AiLineupSelectionService.BuildRealisticLineup(fixture.HomeTeam);
        }

        if (!IsHumanControlled(fixture.AwayTeam, options))
        {
            AiLineupSelectionService.BuildRealisticLineup(fixture.AwayTeam);
        }

        _ = LineupValidationService.RepairUnavailablePlayers(fixture.HomeTeam);
        _ = LineupValidationService.RepairUnavailablePlayers(fixture.AwayTeam);
    }

    private static bool IsHumanControlled(Team team, MatchSimulationOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.HumanControlledTeamName) &&
            string.Equals(team.Name, options.HumanControlledTeamName, StringComparison.OrdinalIgnoreCase);
    }

    private static MatchSimulationOptions CreateOptions(MatchSimulationOptions? options)
    {
        return options ?? new MatchSimulationOptions();
    }
}
