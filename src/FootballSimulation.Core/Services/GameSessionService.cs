using FootballSimulation.Engine;
using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class GameSessionService
{
    public const string PremierLeagueName = "Premier League";

    private readonly LeagueEngine _leagueEngine;

    public GameSessionService()
        : this(new LeagueEngine())
    {
    }

    public GameSessionService(LeagueEngine leagueEngine)
    {
        _leagueEngine = leagueEngine;
    }

    public League CreatePremierLeague(List<Team> teams)
    {
        return _leagueEngine.CreateLeague(PremierLeagueName, teams);
    }

    public Fixture FindNextFixtureForTeam(League league, Team selectedTeam)
    {
        return league.Fixtures
            .OrderBy(fixture => fixture.RoundNumber)
            .First(fixture => !fixture.IsPlayed && IsTeamInFixture(fixture, selectedTeam));
    }

    public Match SimulateSelectedTeamFixture(League league, Team selectedTeam)
    {
        var fixture = FindNextFixtureForTeam(league, selectedTeam);
        var result = _leagueEngine.SimulateFixture(league, fixture, options: CreateUserMatchOptions(selectedTeam));
        _leagueEngine.SimulateRemainingFixturesInRound(league, fixture.RoundNumber);

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
        _leagueEngine.SimulateRemainingFixturesInRound(league, fixture.RoundNumber);

        return result;
    }

    public void CompleteSelectedTeamLiveMatch(League league, Fixture fixture, Match match)
    {
        _leagueEngine.CompleteLiveFixture(league, fixture, match);
        _leagueEngine.SimulateRemainingFixturesInRound(league, fixture.RoundNumber);
    }

    public List<Match> SimulateRemainingRoundFixtures(League league, int roundNumber)
    {
        return _leagueEngine.SimulateRemainingFixturesInRound(league, roundNumber);
    }

    private static bool IsTeamInFixture(Fixture fixture, Team team)
    {
        return fixture.HomeTeam == team || fixture.AwayTeam == team;
    }

    private static MatchSimulationOptions CreateUserMatchOptions(Team selectedTeam)
    {
        return new MatchSimulationOptions
        {
            HumanControlledTeamName = selectedTeam.Name
        };
    }
}
