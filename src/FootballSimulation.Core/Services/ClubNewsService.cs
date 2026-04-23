using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class ClubNewsService
{
    public List<ClubNewsItem> GenerateClubNews(League league, Team selectedTeam)
    {
        var recentResults = new RecentResultService().GetRecentResults(league, selectedTeam);
        var newsItems = new List<ClubNewsItem>();

        AddLatestNews(newsItems, selectedTeam, recentResults);
        AddRumours(newsItems, selectedTeam);
        AddMediaComments(newsItems, selectedTeam, recentResults);

        return newsItems;
    }

    private static void AddLatestNews(List<ClubNewsItem> newsItems, Team team, List<RecentMatchResult> recentResults)
    {
        var injuredPlayers = team.Players.Where(player => player.IsInjured).Take(2).ToList();
        if (injuredPlayers.Count > 0)
        {
            newsItems.Add(new ClubNewsItem
            {
                Type = ClubNewsType.News,
                Headline = "Injury concerns in the squad",
                Details = $"{string.Join(", ", injuredPlayers.Select(player => player.Name))} will need monitoring before the next fixture."
            });
        }

        if (recentResults.Count == 0)
        {
            newsItems.Add(new ClubNewsItem
            {
                Type = ClubNewsType.News,
                Headline = "Season story begins here",
                Details = $"{team.Name} have no played matches yet. Supporters are waiting to see the manager's first competitive result."
            });
            return;
        }

        var latest = recentResults[0];
        newsItems.Add(new ClubNewsItem
        {
            Type = ClubNewsType.News,
            Headline = latest.ResultType switch
            {
                "W" => "Confidence rises after latest win",
                "L" => "Pressure builds after defeat",
                _ => "Mixed reaction after draw"
            },
            Details = $"{team.Name} recorded a {latest.ScoreText} result against {latest.OpponentName}."
        });
    }

    private static void AddRumours(List<ClubNewsItem> newsItems, Team team)
    {
        var tiredPlayers = team.Players
            .Where(player => player.Fatigue >= 65 || player.MatchesPlayedRecently >= 4)
            .Take(2)
            .ToList();

        newsItems.Add(new ClubNewsItem
        {
            Type = ClubNewsType.Rumour,
            Headline = "Recruitment team watches squad depth",
            Details = tiredPlayers.Count == 0
                ? "Club sources suggest the squad is balanced, but scouts are still monitoring late market opportunities."
                : $"Reports suggest depth could be targeted as {string.Join(", ", tiredPlayers.Select(player => player.Name))} carry heavy workloads."
        });
    }

    private static void AddMediaComments(List<ClubNewsItem> newsItems, Team team, List<RecentMatchResult> recentResults)
    {
        var comments = recentResults.Take(3).Select(result => result.ResultType).ToList();
        var wins = comments.Count(result => result == "W");
        var losses = comments.Count(result => result == "L");

        newsItems.Add(new ClubNewsItem
        {
            Type = ClubNewsType.MediaComment,
            Headline = wins > losses ? "Media praise tactical direction" : losses > wins ? "Pundits question consistency" : "Analysts split on current form",
            Details = recentResults.Count == 0
                ? $"The media want to see how {team.Name} handle the first serious test of the season."
                : $"{team.Name}'s recent run has produced {wins} win(s), {losses} defeat(s), and {comments.Count - wins - losses} draw(s)."
        });
    }
}
