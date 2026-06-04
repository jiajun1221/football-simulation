using FootballSimulation.Engine;
using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class GameSessionService
{
    public const string PremierLeagueName = "Premier League";

    private readonly LeagueEngine _leagueEngine;
    private readonly PlayerFormPersistenceService _playerFormPersistenceService;
    private readonly InjuryRecoveryService _injuryRecoveryService = new();
    private readonly FatigueService _fatigueService = new();
    private readonly PlayerGrowthService _playerGrowthService = new();

    public GameSessionService()
        : this(new LeagueEngine(), new PlayerFormPersistenceService())
    {
    }

    public GameSessionService(
        LeagueEngine leagueEngine,
        PlayerFormPersistenceService? playerFormPersistenceService = null)
    {
        _leagueEngine = leagueEngine;
        _playerFormPersistenceService = playerFormPersistenceService ?? new PlayerFormPersistenceService();
    }

    public League CreatePremierLeague(List<Team> teams)
    {
        return CreateLeague(new LeagueDefinition
        {
            LeagueId = LeagueDataService.DefaultLeagueId,
            Name = PremierLeagueName,
            Season = "2025-26"
        }, teams);
    }

    public League CreateLeague(LeagueDefinition definition, List<Team> teams)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return _leagueEngine.CreateLeague(definition.LeagueId, definition.Name, definition.Season, teams);
    }

    public League CreateLeague(string leagueName, List<Team> teams)
    {
        return _leagueEngine.CreateLeague(leagueName, teams);
    }

    public Fixture FindNextFixtureForTeam(League league, Team selectedTeam)
    {
        return league.Fixtures
            .OrderBy(GetFixtureCalendarRound)
            .ThenBy(fixture => fixture.Competition)
            .First(fixture => !fixture.IsPlayed && IsTeamInFixture(fixture, selectedTeam));
    }

    public Match SimulateSelectedTeamFixture(League league, Team selectedTeam)
    {
        var fixture = FindNextFixtureForTeam(league, selectedTeam);
        var result = _leagueEngine.SimulateFixture(league, fixture, options: CreateUserMatchOptions(selectedTeam));
        var remainingResults = _leagueEngine.SimulateRemainingFixturesForCalendarSlot(league, GetFixtureCalendarRound(fixture));
        ApplyGrowthForCompletedMatches([result, .. remainingResults]);
        AdvanceRecoveryAfterCalendarSlot(league, fixture);
        _playerFormPersistenceService.SaveActiveSquadFormStatuses(league.Teams, league.LeagueId);

        return result;
    }

    public Match SimulateSelectedTeamFirstHalf(League league, Team selectedTeam)
    {
        var fixture = FindNextFixtureForTeam(league, selectedTeam);
        return _leagueEngine.SimulateFixtureFirstHalf(league, fixture, options: CreateUserMatchOptions(selectedTeam));
    }

    public Match CreateSelectedTeamLiveMatch(League league, Team selectedTeam)
    {
        var fixture = FindNextFixtureForTeam(league, selectedTeam);
        return _leagueEngine.CreateLiveFixtureMatch(league, fixture, CreateUserMatchOptions(selectedTeam));
    }

    public Match AdvanceSelectedTeamLiveMatch(
        League league,
        Fixture fixture,
        Match match,
        Team selectedTeam,
        int startMinute,
        int endMinute,
        bool includeFulltime)
    {
        return _leagueEngine.AdvanceLiveFixture(
            league,
            fixture,
            match,
            startMinute,
            endMinute,
            includeFulltime,
            options: CreateUserMatchOptions(selectedTeam));
    }

    public Match SimulateSelectedTeamSecondHalf(League league, Fixture fixture, Match match, Team? selectedTeam = null)
    {
        var humanTeam = selectedTeam ?? fixture.HomeTeam;
        var result = _leagueEngine.SimulateFixtureSecondHalf(league, fixture, match, options: CreateUserMatchOptions(humanTeam));
        var remainingResults = _leagueEngine.SimulateRemainingFixturesForCalendarSlot(league, GetFixtureCalendarRound(fixture));
        ApplyGrowthForCompletedMatches([result, .. remainingResults]);
        AdvanceRecoveryAfterCalendarSlot(league, fixture);
        _playerFormPersistenceService.SaveActiveSquadFormStatuses(league.Teams, league.LeagueId);

        return result;
    }

    public void CompleteSelectedTeamLiveMatch(League league, Fixture fixture, Match match)
    {
        _leagueEngine.CompleteLiveFixture(league, fixture, match);
        var remainingResults = _leagueEngine.SimulateRemainingFixturesForCalendarSlot(league, GetFixtureCalendarRound(fixture));
        ApplyGrowthForCompletedMatches([match, .. remainingResults]);
        AdvanceRecoveryAfterCalendarSlot(league, fixture);
        _playerFormPersistenceService.SaveActiveSquadFormStatuses(league.Teams, league.LeagueId);
    }

    public List<Match> SimulateRemainingRoundFixtures(League league, int roundNumber)
    {
        var results = _leagueEngine.SimulateRemainingFixturesInRound(league, roundNumber);
        ApplyGrowthForCompletedMatches(results);
        AdvanceRecoveryAfterCalendarSlot(league, roundNumber);
        _playerFormPersistenceService.SaveActiveSquadFormStatuses(league.Teams, league.LeagueId);
        return results;
    }

    private void AdvanceRecoveryAfterCalendarSlot(League league, Fixture completedFixture)
    {
        AdvanceRecoveryAfterCalendarSlot(league, GetFixtureCalendarRound(completedFixture));
    }

    private void AdvanceRecoveryAfterCalendarSlot(League league, int completedCalendarRound)
    {
        _injuryRecoveryService.AdvanceRecoveryAfterCompletedRound(league.Teams);

        foreach (var team in league.Teams)
        {
            var nextFixture = league.Fixtures
                .Where(fixture => !fixture.IsPlayed && IsTeamInFixture(fixture, team))
                .OrderBy(GetFixtureCalendarRound)
                .FirstOrDefault();
            if (nextFixture is null)
            {
                _fatigueService.RecoverTeamForNewMatch(team, 60);
                continue;
            }

            var gap = Math.Max(1, GetFixtureCalendarRound(nextFixture) - completedCalendarRound);
            _fatigueService.RecoverTeamForNewMatch(team, CalculateRecoveryPoints(gap));
        }
    }

    private void ApplyGrowthForCompletedMatches(IEnumerable<Match> matches)
    {
        foreach (var match in matches)
        {
            _playerGrowthService.ApplyMatchGrowth(match);
        }
    }

    private static bool IsTeamInFixture(Fixture fixture, Team team)
    {
        return fixture.HomeTeam.Name.Equals(team.Name, StringComparison.OrdinalIgnoreCase) ||
            fixture.AwayTeam.Name.Equals(team.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetFixtureCalendarRound(Fixture fixture)
    {
        return fixture.CalendarRound > 0 ? fixture.CalendarRound : fixture.RoundNumber;
    }

    private static int CalculateRecoveryPoints(int calendarGap)
    {
        return calendarGap switch
        {
            <= 1 => 12,
            <= 3 => 24,
            <= 6 => 40,
            <= 10 => 52,
            _ => 60
        };
    }

    private static MatchSimulationOptions CreateUserMatchOptions(Team selectedTeam)
    {
        return new MatchSimulationOptions
        {
            HumanControlledTeamName = selectedTeam.Name
        };
    }
}
