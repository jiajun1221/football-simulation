using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class ClubFinanceService
{
    private static readonly HashSet<string> EliteClubs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Real Madrid",
        "Manchester City",
        "Paris Saint-Germain",
        "PSG"
    };

    private static readonly HashSet<string> BigClubs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Chelsea",
        "Arsenal",
        "Barcelona",
        "Bayern Munich",
        "Liverpool",
        "Manchester United",
        "Inter Milan",
        "AC Milan",
        "Juventus",
        "Atletico Madrid",
        "Borussia Dortmund",
        "Tottenham Hotspur",
        "Newcastle United",
        "Napoli",
        "Roma",
        "Marseille",
        "Monaco"
    };

    public ClubFinance CreateFinance(string leagueId, Team team)
    {
        var averageRating = team.Players.Concat(team.Substitutes).DefaultIfEmpty().Average(player => player?.OverallRating ?? 72);
        var budget = GetBaseBudget(team.Name, averageRating);

        return new ClubFinance
        {
            LeagueId = leagueId,
            ClubName = team.Name,
            ClubTransferBudget = budget,
            ClubWageBudget = Math.Round(budget * 0.22m, 0),
            TransferSpent = 0,
            TransferIncome = 0,
            WageSpent = CalculateWageSpent(team)
        };
    }

    public ClubFinance GetOrCreateFinance(TransferMarketState state, string leagueId, Team team)
    {
        var finance = state.ClubFinances.FirstOrDefault(item =>
            item.LeagueId.Equals(leagueId, StringComparison.OrdinalIgnoreCase) &&
            item.ClubName.Equals(team.Name, StringComparison.OrdinalIgnoreCase));

        if (finance is not null)
        {
            finance.WageSpent = CalculateWageSpent(team);
            return finance;
        }

        finance = CreateFinance(leagueId, team);
        state.ClubFinances.Add(finance);
        return finance;
    }

    private static decimal GetBaseBudget(string clubName, double averageRating)
    {
        if (EliteClubs.Contains(clubName))
        {
            return 180_000_000m;
        }

        if (BigClubs.Contains(clubName))
        {
            return 120_000_000m;
        }

        return averageRating switch
        {
            >= 82 => 85_000_000m,
            >= 78 => 55_000_000m,
            >= 74 => 32_000_000m,
            _ => 18_000_000m
        };
    }

    public static decimal CalculateWageSpent(Team team)
    {
        return team.Players
            .Concat(team.Substitutes)
            .Sum(player => player.WeeklyWage ?? 0);
    }
}
