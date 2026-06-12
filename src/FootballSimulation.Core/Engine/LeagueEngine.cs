using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Engine;

public class LeagueEngine
{
    private readonly MatchEngine _matchEngine;
    private readonly LeagueScheduleService _scheduleService;
    private readonly LeagueTableService _tableService;
    private readonly SeasonCalendarService _seasonCalendarService = new();
    private readonly CompetitionProgressionService _competitionProgressionService = new();
    private readonly PlayerFormStatusService _playerFormStatusService;
    private readonly PlayerSeasonStatsService _playerSeasonStatsService = new();
    private readonly InjuryRiskService _injuryRiskService = new();
    private readonly YouthAcademyService _youthAcademyService = new();
    private readonly YouthScoutService _youthScoutService = new();

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

        var league = new League
        {
            LeagueId = leagueId,
            Name = leagueName,
            Season = season,
            Teams = teams,
            Fixtures = _seasonCalendarService.GenerateSeasonFixtures(teams, season),
            Table = _tableService.CreateTable(teams),
            CompetitionStates = _seasonCalendarService.CreateInitialCompetitionStates(teams)
        };
        _youthAcademyService.EnsureAcademies(league);
        _youthAcademyService.GenerateSeasonalIntake(league, season);
        _youthScoutService.EnsureScoutNetwork(league);
        return league;
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
        PrepareFixtureTeams(fixture, simulationOptions, CreatePreparationRandom(matchSeed));
        var result = _matchEngine.SimulateMatch(fixture.HomeTeam, fixture.AwayTeam, matchSeed, options: simulationOptions);
        _injuryRiskService.ApplyPostMatchLoad(result);
        _playerFormStatusService.UpdateMatchPlayerFormStatuses(result);

        CompleteFixtureProgression(league, fixture, result, matchSeed);

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
        PrepareFixtureTeams(fixture, simulationOptions, CreatePreparationRandom(matchSeed));
        return _matchEngine.SimulateFirstHalf(fixture.HomeTeam, fixture.AwayTeam, matchSeed, simulationOptions);
    }

    public Match CreateLiveFixtureMatch(League league, Fixture fixture, MatchSimulationOptions? options = null)
    {
        ValidateFixtureIsPlayable(league, fixture);
        var simulationOptions = CreateOptions(options);
        PrepareFixtureTeams(fixture, simulationOptions, Random.Shared);
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
        _injuryRiskService.ApplyPostMatchLoad(result);
        _playerFormStatusService.UpdateMatchPlayerFormStatuses(result);

        CompleteFixtureProgression(league, fixture, result, matchSeed);

        return result;
    }

    public void CompleteLiveFixture(League league, Fixture fixture, Match match)
    {
        ValidateFixtureIsPlayable(league, fixture);

        _injuryRiskService.ApplyPostMatchLoad(match);
        _playerFormStatusService.UpdateMatchPlayerFormStatuses(match);

        CompleteFixtureProgression(league, fixture, match);
    }

    public List<Match> SimulateRemainingFixturesInRound(League league, int roundNumber, int? seed = null, MatchSimulationOptions? options = null)
    {
        var results = new List<Match>();
        var roundFixtures = league.Fixtures
            .Where(fixture => fixture.Competition == CompetitionType.PremierLeague &&
                fixture.RoundNumber == roundNumber &&
                !fixture.IsPlayed)
            .ToList();

        foreach (var fixture in roundFixtures)
        {
            results.Add(SimulateFixture(league, fixture, seed, options));
        }

        return results;
    }

    public List<Match> SimulateRemainingFixturesForCalendarSlot(League league, int calendarRound, int? seed = null, MatchSimulationOptions? options = null)
    {
        var results = new List<Match>();
        var roundFixtures = league.Fixtures
            .Where(fixture => GetFixtureCalendarRound(fixture) == calendarRound && !fixture.IsPlayed)
            .ToList();

        foreach (var fixture in roundFixtures)
        {
            results.Add(SimulateFixture(league, fixture, seed, options));
        }

        return results;
    }

    public List<Match> SimulateRemainingFixturesForCompetitionRound(
        League league,
        Fixture completedFixture,
        int? seed = null,
        MatchSimulationOptions? options = null)
    {
        var results = new List<Match>();
        var roundFixtures = league.Fixtures
            .Where(fixture => !fixture.IsPlayed && IsSameCompetitionRound(fixture, completedFixture))
            .ToList();

        foreach (var fixture in roundFixtures)
        {
            results.Add(SimulateFixture(league, fixture, seed, options));
        }

        return results;
    }

    private void CompleteFixtureProgression(League league, Fixture fixture, Match result, int? seed = null)
    {
        _competitionProgressionService.EnsureFixtureMetadata(fixture);
        fixture.Result = result;
        fixture.IsPlayed = true;
        _competitionProgressionService.ProcessCompletedFixture(league, fixture, seed);
        _playerSeasonStatsService.RebuildLeagueSeasonStats(league);
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

    private void PrepareFixtureTeams(Fixture fixture, MatchSimulationOptions options, Random random)
    {
        if (options.EnableInjuries)
        {
            _ = _injuryRiskService.TryCreatePreparationInjury(fixture.HomeTeam, GetFixtureCalendarRound(fixture), random);
            _ = _injuryRiskService.TryCreatePreparationInjury(fixture.AwayTeam, GetFixtureCalendarRound(fixture), random);
        }

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
        if (options is null)
        {
            return new MatchSimulationOptions
            {
                PreserveMatchStartStamina = true
            };
        }

        options.PreserveMatchStartStamina = true;
        return options;
    }

    private static Random CreatePreparationRandom(int? matchSeed)
    {
        return matchSeed.HasValue
            ? new Random(unchecked(matchSeed.Value * 397 ^ 0x2F6E2B1))
            : Random.Shared;
    }

    private static int GetFixtureCalendarRound(Fixture fixture)
    {
        return fixture.CalendarRound > 0 ? fixture.CalendarRound : fixture.RoundNumber;
    }

    private static bool IsSameCompetitionRound(Fixture fixture, Fixture completedFixture)
    {
        if (fixture.Competition != completedFixture.Competition)
        {
            return false;
        }

        if (fixture.Competition == CompetitionType.PremierLeague ||
            fixture.Competition == CompetitionType.ChampionsLeague && !fixture.IsKnockout && !completedFixture.IsKnockout)
        {
            return fixture.RoundNumber == completedFixture.RoundNumber;
        }

        return !string.IsNullOrWhiteSpace(fixture.RoundName) &&
            fixture.RoundName.Equals(completedFixture.RoundName, StringComparison.OrdinalIgnoreCase);
    }
}
