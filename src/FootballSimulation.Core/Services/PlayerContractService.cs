using System.Globalization;
using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class PlayerContractService
{
    public const int DefaultSeasonEndYear = 2026;

    public static void ApplyContractData(
        Player player,
        string leagueId,
        int? contractEndYear = null,
        decimal? weeklyWage = null,
        decimal? releaseClause = null,
        PlayerContractStatus? contractStatus = null)
    {
        player.ContractEndYear = contractEndYear ?? player.ContractEndYear;
        player.WeeklyWage = weeklyWage is > 0 ? weeklyWage : player.WeeklyWage;
        player.ReleaseClause = releaseClause is > 0 ? releaseClause : player.ReleaseClause;
        if (contractStatus.HasValue)
        {
            player.ContractStatus = contractStatus.Value;
        }

        EnsureContract(player, leagueId);
    }

    public static void EnsureContract(Player player, string leagueId, int seasonEndYear = DefaultSeasonEndYear, int currentRound = 1, int totalRounds = 38)
    {
        player.ContractEndYear ??= seasonEndYear + GetDefaultYears(player);
        player.WeeklyWage ??= EstimateWeeklyWage(player, leagueId);
        if (player.ReleaseClause is <= 0)
        {
            player.ReleaseClause = null;
        }

        player.ContractStatus = GetContractStatus(player, seasonEndYear, currentRound, totalRounds);
    }

    public static PlayerContractStatus GetContractStatus(Player player, int seasonEndYear = DefaultSeasonEndYear, int currentRound = 1, int totalRounds = 38)
    {
        if (player.ContractEndYear is null)
        {
            return PlayerContractStatus.Active;
        }

        if (player.ContractEndYear < seasonEndYear)
        {
            return PlayerContractStatus.FreeAgent;
        }

        if (player.ContractEndYear == seasonEndYear)
        {
            return currentRound >= Math.Max(1, totalRounds / 2)
                ? PlayerContractStatus.PreContractEligible
                : PlayerContractStatus.ExpiringSoon;
        }

        return player.ContractEndYear == seasonEndYear + 1
            ? PlayerContractStatus.ExpiringSoon
            : PlayerContractStatus.Active;
    }

    public static int GetYearsRemaining(Player player, int seasonEndYear = DefaultSeasonEndYear)
    {
        return Math.Max(0, (player.ContractEndYear ?? seasonEndYear) - seasonEndYear);
    }

    public static bool IsExpiringSoon(Player player, int seasonEndYear = DefaultSeasonEndYear)
    {
        return GetYearsRemaining(player, seasonEndYear) <= 1;
    }

    public static bool IsPreContractEligible(Player player, int seasonEndYear = DefaultSeasonEndYear, int currentRound = 1, int totalRounds = 38)
    {
        return GetContractStatus(player, seasonEndYear, currentRound, totalRounds) == PlayerContractStatus.PreContractEligible;
    }

    public static decimal EstimateWeeklyWage(Player player, string leagueId)
    {
        var baseWage = player.OverallRating switch
        {
            >= 94 => 520_000m,
            >= 91 => 360_000m,
            >= 88 => 230_000m,
            >= 85 => 150_000m,
            >= 82 => 95_000m,
            >= 79 => 62_000m,
            >= 76 => 38_000m,
            >= 72 => 22_000m,
            >= 68 => 12_000m,
            _ => 7_000m
        };

        var leagueModifier = leagueId switch
        {
            "premier-league" => 1.18m,
            "la-liga" => 1.08m,
            "serie-a" => 0.98m,
            "bundesliga" => 0.98m,
            "ligue-1" => 0.95m,
            _ => 1.0m
        };
        var roleModifier = player.Role switch
        {
            PlayerRole.KeyPlayer => 1.28m,
            PlayerRole.Starter => 1.10m,
            PlayerRole.Prospect => 0.82m,
            PlayerRole.Backup => 0.72m,
            _ => 1.0m
        };
        var ageModifier = player.Age switch
        {
            <= 21 => 0.78m,
            >= 32 => 0.86m,
            _ => 1.0m
        };

        return RoundWage(baseWage * leagueModifier * roleModifier * ageModifier);
    }

    public static decimal EstimateReleaseClause(Player player, decimal marketValue, string leagueId)
    {
        if (leagueId is not ("la-liga" or "ligue-1") || player.Role is PlayerRole.Backup or PlayerRole.Rotation)
        {
            return 0;
        }

        var multiplier = player.Role == PlayerRole.KeyPlayer ? 2.1m : 1.75m;
        return RoundMoney(marketValue * multiplier);
    }

    public static decimal GetContractMarketModifier(Player player, int seasonEndYear = DefaultSeasonEndYear)
    {
        if (player.ContractEndYear is null)
        {
            return 1.0m;
        }

        return GetYearsRemaining(player, seasonEndYear) switch
        {
            0 => 0.50m,
            1 => 0.76m,
            2 => 0.94m,
            3 => 1.04m,
            >= 4 => 1.10m,
            _ => 1.0m
        };
    }

    public static decimal GetAskingPriceModifier(Player player, int seasonEndYear = DefaultSeasonEndYear)
    {
        if (player.ContractEndYear is null)
        {
            return 1.0m;
        }

        var years = GetYearsRemaining(player, seasonEndYear);
        var modifier = years switch
        {
            0 => 0.45m,
            1 => 0.72m,
            2 => 0.95m,
            3 => 1.05m,
            >= 4 => 1.16m,
            _ => 1.0m
        };

        if (years >= 3 && player.Role == PlayerRole.KeyPlayer)
        {
            modifier += 0.10m;
        }

        if (player.Morale < 35)
        {
            modifier -= 0.08m;
        }

        return Math.Clamp(modifier, 0.42m, 1.32m);
    }

    public static string FormatWage(decimal weeklyWage)
    {
        return weeklyWage >= 1_000_000
            ? $"£{weeklyWage / 1_000_000m:0.#}M/w"
            : $"£{weeklyWage / 1_000m:0}k/w";
    }

    public static string FormatContractExpiry(Player player)
    {
        return player.ContractEndYear is null ? "Contract N/A" : $"Expires {player.ContractEndYear.Value.ToString(CultureInfo.InvariantCulture)}";
    }

    public static string FormatRole(PlayerRole role)
    {
        return role switch
        {
            PlayerRole.KeyPlayer => "Key Player",
            PlayerRole.Starter => "Important Player",
            PlayerRole.Rotation => "Rotation Player",
            PlayerRole.Prospect => "Prospect",
            PlayerRole.Backup => "Backup",
            _ => role.ToString()
        };
    }

    private static int GetDefaultYears(Player player)
    {
        if (player.Age >= 33)
        {
            return 1;
        }

        if (player.Age >= 30)
        {
            return 2;
        }

        return player.Role switch
        {
            PlayerRole.KeyPlayer => 4,
            PlayerRole.Starter => 3,
            PlayerRole.Prospect => 4,
            PlayerRole.Backup => 2,
            _ => 3
        };
    }

    private static decimal RoundWage(decimal value)
    {
        var step = value >= 100_000 ? 5_000m : 1_000m;
        return Math.Max(1_000m, Math.Round(value / step, MidpointRounding.AwayFromZero) * step);
    }

    private static decimal RoundMoney(decimal value)
    {
        var step = value >= 20_000_000 ? 500_000m : 100_000m;
        return Math.Round(value / step, MidpointRounding.AwayFromZero) * step;
    }
}
