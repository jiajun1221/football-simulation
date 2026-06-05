using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class ClubFinanceService
{
    private static readonly HashSet<string> EliteClubs = new(StringComparer.OrdinalIgnoreCase)
    {
        "Real Madrid",
        "Manchester City",
        "Paris Saint-Germain",
        "PSG",
        "Bayern Munich",
        "Barcelona"
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
        "Bayer Leverkusen",
        "Tottenham Hotspur",
        "Newcastle United",
        "Napoli",
        "Benfica",
        "Porto",
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

    public BudgetRolloverSummary ApplySeasonRolloverBudget(
        TransferMarketState state,
        string leagueId,
        Team team,
        int finalPosition,
        int teamCount)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(team);

        var finance = GetOrCreateFinance(state, leagueId, team);
        var summary = CreateBudgetRolloverSummary(finance, team, finalPosition, teamCount);

        finance.ClubTransferBudget = summary.NewBudget;
        finance.ClubWageBudget = Math.Round(summary.NewBudget * 0.22m, 0);
        finance.TransferSpent = 0;
        finance.TransferIncome = 0;
        finance.WageSpent = CalculateWageSpent(team);

        return summary;
    }

    public BudgetRolloverSummary PreviewSeasonRolloverBudget(
        TransferMarketState state,
        string leagueId,
        Team team,
        int finalPosition,
        int teamCount)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(team);

        var finance = GetOrCreateFinance(state, leagueId, team);
        return CreateBudgetRolloverSummary(finance, team, finalPosition, teamCount);
    }

    public static decimal GetBaseBudget(string clubName, double averageRating)
    {
        if (IsEliteClub(clubName))
        {
            return 180_000_000m;
        }

        if (IsBigClub(clubName))
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

    public static bool IsEliteClub(string clubName)
    {
        return EliteClubs.Contains(NormalizeClubAlias(clubName));
    }

    public static bool IsBigClub(string clubName)
    {
        var normalized = NormalizeClubAlias(clubName);
        return BigClubs.Contains(normalized) || EliteClubs.Contains(normalized);
    }

    public static int GetReputationScore(string clubName, double averageRating)
    {
        if (IsEliteClub(clubName))
        {
            return 96;
        }

        if (IsBigClub(clubName))
        {
            return 88;
        }

        return averageRating switch
        {
            >= 82 => 82,
            >= 78 => 76,
            >= 74 => 68,
            _ => 58
        };
    }

    public static double GetTransferActivityWeight(string clubName, decimal availableBudget, double averageRating)
    {
        var weight = IsEliteClub(clubName) ? 3.2 : IsBigClub(clubName) ? 2.35 : 1.0;
        weight += availableBudget switch
        {
            >= 150_000_000m => 0.85,
            >= 90_000_000m => 0.55,
            >= 50_000_000m => 0.25,
            _ => 0.0
        };
        weight += averageRating switch
        {
            >= 83 => 0.45,
            >= 79 => 0.25,
            _ => 0.0
        };

        return weight;
    }

    public static decimal CalculateWageSpent(Team team)
    {
        return team.Players
            .Concat(team.Substitutes)
            .Sum(player => player.WeeklyWage ?? 0);
    }

    private static string NormalizeClubAlias(string clubName)
    {
        return clubName.Equals("Paris Saint-Germain", StringComparison.OrdinalIgnoreCase) ? "PSG" : clubName;
    }

    private static BudgetRolloverSummary CreateBudgetRolloverSummary(
        ClubFinance finance,
        Team team,
        int finalPosition,
        int teamCount)
    {
        var averageRating = team.Players.Concat(team.Substitutes).DefaultIfEmpty().Average(player => player?.OverallRating ?? 72);
        var carryover = Math.Round(finance.AvailableTransferBudget * 0.5m, 0);
        var baseBudget = GetBaseBudget(team.Name, averageRating);
        if (IsRelegated(finalPosition, teamCount))
        {
            baseBudget = Math.Round(baseBudget * 0.65m, 0);
        }

        var performanceBonus = GetPerformanceBonus(finalPosition, teamCount);
        var qualificationBonus = GetQualificationBonus(finalPosition, teamCount);
        var newBudget = Math.Max(8_000_000m, carryover + baseBudget + performanceBonus + qualificationBonus);

        return new BudgetRolloverSummary
        {
            ClubName = team.Name,
            RemainingCarryover = carryover,
            BaseBudget = baseBudget,
            PerformanceBonus = performanceBonus,
            QualificationBonus = qualificationBonus,
            NewBudget = newBudget,
            Qualification = GetQualificationLabel(finalPosition, teamCount)
        };
    }

    private static decimal GetPerformanceBonus(int finalPosition, int teamCount)
    {
        if (finalPosition == 1)
        {
            return 60_000_000m;
        }

        if (finalPosition <= 4)
        {
            return 25_000_000m;
        }

        if (finalPosition == 5)
        {
            return 12_000_000m;
        }

        if (finalPosition == 6)
        {
            return 6_000_000m;
        }

        return IsRelegated(finalPosition, teamCount) ? -5_000_000m : 0;
    }

    private static decimal GetQualificationBonus(int finalPosition, int teamCount)
    {
        if (finalPosition <= 0 || IsRelegated(finalPosition, teamCount))
        {
            return 0;
        }

        return finalPosition switch
        {
            <= 4 => 40_000_000m,
            5 => 25_000_000m,
            6 => 15_000_000m,
            _ => 0
        };
    }

    private static string GetQualificationLabel(int finalPosition, int teamCount)
    {
        if (finalPosition == 1)
        {
            return "Champions League - Champions";
        }

        if (finalPosition is >= 2 and <= 4)
        {
            return "Champions League";
        }

        if (finalPosition == 5)
        {
            return "Europa League";
        }

        if (finalPosition == 6)
        {
            return "Conference League";
        }

        if (IsRelegated(finalPosition, teamCount))
        {
            return "Relegated";
        }

        return "No European qualification";
    }

    private static bool IsRelegated(int finalPosition, int teamCount)
    {
        return finalPosition > 0 && finalPosition > teamCount - 3;
    }
}
