using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class LeagueTableService
{
    public List<LeagueTableEntry> CreateTable(IEnumerable<Team> teams)
    {
        return teams
            .Select(team => new LeagueTableEntry
            {
                TeamName = team.Name
            })
            .ToList();
    }

    public void ApplyMatchResult(List<LeagueTableEntry> table, Match match)
    {
        var homeEntry = table.Single(entry => entry.TeamName == match.HomeTeam.Name);
        var awayEntry = table.Single(entry => entry.TeamName == match.AwayTeam.Name);

        homeEntry.Played++;
        awayEntry.Played++;

        homeEntry.GoalsFor += match.HomeScore;
        homeEntry.GoalsAgainst += match.AwayScore;
        awayEntry.GoalsFor += match.AwayScore;
        awayEntry.GoalsAgainst += match.HomeScore;

        if (match.HomeScore > match.AwayScore)
        {
            homeEntry.Wins++;
            homeEntry.Points += 3;
            awayEntry.Losses++;
        }
        else if (match.HomeScore < match.AwayScore)
        {
            awayEntry.Wins++;
            awayEntry.Points += 3;
            homeEntry.Losses++;
        }
        else
        {
            homeEntry.Draws++;
            awayEntry.Draws++;
            homeEntry.Points++;
            awayEntry.Points++;
        }
    }

    public List<LeagueTableEntry> SortTable(IEnumerable<LeagueTableEntry> table)
    {
        return table
            .OrderByDescending(entry => entry.Points)
            .ThenByDescending(entry => entry.GoalDifference)
            .ThenByDescending(entry => entry.GoalsFor)
            .ThenBy(entry => entry.TeamName)
            .ToList();
    }
}
