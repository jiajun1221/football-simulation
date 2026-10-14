using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class RecentResultService
{
    public List<RecentMatchResult> GetRecentResults(
        League league,
        Team selectedTeam,
        int count = 5,
        CompetitionType? competition = null)
    {
        return league.Fixtures
            .Where(fixture => fixture.IsPlayed && fixture.Result is not null && IsTeamInFixture(fixture, selectedTeam))
            .Where(fixture => competition is null || fixture.Competition == competition)
            .OrderByDescending(GetFixtureSortRound)
            .Take(count)
            .Select(fixture => CreateRecentResult(fixture, selectedTeam))
            .ToList();
    }

    private static RecentMatchResult CreateRecentResult(Fixture fixture, Team selectedTeam)
    {
        var match = fixture.Result!;
        var isHome = fixture.HomeTeam == selectedTeam;
        var selectedGoals = isHome ? match.HomeScore : match.AwayScore;
        var opponentGoals = isHome ? match.AwayScore : match.HomeScore;
        var opponent = isHome ? fixture.AwayTeam : fixture.HomeTeam;

        return new RecentMatchResult
        {
            RoundNumber = fixture.RoundNumber,
            OpponentName = opponent.Name,
            ScoreText = $"{selectedGoals} - {opponentGoals}",
            ResultType = selectedGoals > opponentGoals ? "W" : selectedGoals < opponentGoals ? "L" : "D"
        };
    }

    private static bool IsTeamInFixture(Fixture fixture, Team team)
    {
        return fixture.HomeTeam == team ||
            fixture.AwayTeam == team ||
            fixture.HomeTeam.Name.Equals(team.Name, StringComparison.OrdinalIgnoreCase) ||
            fixture.AwayTeam.Name.Equals(team.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetFixtureSortRound(Fixture fixture)
    {
        return fixture.CalendarRound > 0 ? fixture.CalendarRound : fixture.RoundNumber;
    }
}
