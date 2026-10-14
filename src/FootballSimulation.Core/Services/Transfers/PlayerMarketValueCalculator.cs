using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class PlayerMarketValueCalculator
{
    private static readonly Dictionary<string, double> LeagueReputation = new(StringComparer.OrdinalIgnoreCase)
    {
        ["premier-league"] = 1.08,
        ["la-liga"] = 1.05,
        ["serie-a"] = 1.02,
        ["bundesliga"] = 1.02,
        ["ligue-1"] = 1.0
    };

    public decimal CalculateMarketValue(Player player, string leagueId, IEnumerable<PlayerSeasonStats>? seasonStats = null)
    {
        ArgumentNullException.ThrowIfNull(player);

        var overall = Math.Clamp(player.OverallRating, 45, 99);
        var baseValue = GetBaseValueFromOverall(overall);
        var rawValue = (double)baseValue *
            GetAgeModifier(player) *
            GetFormModifier(player) *
            GetPotentialModifier(player) *
            GetPositionModifier(player) *
            GetReputationModifier(player) *
            GetLeagueModifier(leagueId) *
            GetPerformanceModifier(player, seasonStats) *
            GetAvailabilityModifier(player) *
            (double)PlayerContractService.GetContractMarketModifier(player);

        var cappedValue = Math.Min((decimal)rawValue, GetValueCap(player));
        return RoundToMarketFigure(Math.Max(250_000, cappedValue));
    }

    public decimal CalculateAskingPrice(Player player, string leagueId, IEnumerable<PlayerSeasonStats>? seasonStats = null)
    {
        var marketValue = CalculateMarketValue(player, leagueId, seasonStats);
        var multiplier = 1.0m;
        var potentialGap = Math.Clamp((player.PotentialOverall ?? player.OverallRating) - player.OverallRating, 0, 12);

        multiplier += player.OverallRating switch
        {
            >= 92 => 0.18m,
            >= 89 => 0.12m,
            >= 85 => 0.05m,
            _ => 0m
        };

        if (player.Age is <= 23)
        {
            multiplier += 0.06m + potentialGap * 0.01m;
        }

        multiplier += player.FormStatus switch
        {
            PlayerFormStatus.Excellent => 0.10m,
            PlayerFormStatus.Good => 0.05m,
            PlayerFormStatus.Poor => -0.10m,
            PlayerFormStatus.VeryPoor => -0.16m,
            _ => 0m
        };

        multiplier += GetContractAskingAdjustment(player);

        multiplier += player.Role switch
        {
            PlayerRole.KeyPlayer => 0.18m,
            PlayerRole.Starter => 0.07m,
            PlayerRole.Prospect => 0.05m,
            PlayerRole.Backup => -0.05m,
            _ => 0.04m
        };

        multiplier += GetLeagueAskingModifier(leagueId);

        if (player.TransferStatus == PlayerTransferStatus.Listed)
        {
            multiplier -= 0.15m;
        }

        if (player.Morale < 35)
        {
            multiplier -= 0.08m;
        }

        if (player.Age >= 34)
        {
            multiplier -= 0.18m;
        }
        else if (player.Age >= 31)
        {
            multiplier -= 0.12m;
        }

        if (player.IsInjured || player.IsSuspended)
        {
            multiplier -= 0.08m;
        }

        var cappedMultiplier = Math.Clamp(multiplier, GetMinimumAskingMultiplier(player), GetMaximumAskingMultiplier(player));
        if (player.ReleaseClause is > 0)
        {
            cappedMultiplier = Math.Min(cappedMultiplier, player.ReleaseClause.Value / Math.Max(1, marketValue));
        }

        return RoundToMarketFigure(Math.Min(marketValue * cappedMultiplier, GetAskingPriceCap(player)));
    }

    private static decimal GetBaseValueFromOverall(int overall)
    {
        return overall switch
        {
            >= 95 => 210_000_000m,
            >= 94 => 185_000_000m,
            >= 93 => 165_000_000m,
            >= 92 => 145_000_000m,
            >= 91 => 125_000_000m,
            >= 90 => 105_000_000m,
            >= 89 => 90_000_000m,
            >= 88 => 78_000_000m,
            >= 87 => 66_000_000m,
            >= 86 => 56_000_000m,
            >= 85 => 48_000_000m,
            >= 84 => 40_000_000m,
            >= 83 => 32_000_000m,
            >= 82 => 25_000_000m,
            >= 81 => 20_000_000m,
            >= 80 => 16_000_000m,
            >= 79 => 12_000_000m,
            >= 78 => 9_000_000m,
            >= 77 => 7_000_000m,
            >= 76 => 5_500_000m,
            >= 75 => 4_300_000m,
            >= 74 => 3_400_000m,
            >= 72 => 2_200_000m,
            >= 70 => 1_400_000m,
            >= 65 => 750_000m,
            >= 60 => 450_000m,
            _ => 350_000m
        };
    }

    private static double GetAgeModifier(Player player)
    {
        var age = player.Age ?? 26;
        if (age <= 19) return 1.20;
        if (age <= 21) return 1.18;
        if (age <= 23) return 1.15;
        if (age <= 25) return 1.08;
        if (age <= 29) return 1.0;
        if (age <= 32) return 0.82;
        if (age <= 35) return 0.58;
        return 0.35;
    }

    private static double GetFormModifier(Player player)
    {
        return player.FormStatus switch
        {
            PlayerFormStatus.Excellent => 1.10,
            PlayerFormStatus.Good => 1.04,
            PlayerFormStatus.Poor => 0.94,
            PlayerFormStatus.VeryPoor => 0.85,
            _ => 1.0
        };
    }

    private static double GetPotentialModifier(Player player)
    {
        var potential = player.PotentialOverall ?? player.OverallRating;
        var gap = Math.Clamp(potential - player.OverallRating, 0, 12);
        return 1.0 + gap * 0.025;
    }

    private static double GetPositionModifier(Player player)
    {
        return player.Position switch
        {
            Position.Forward => 1.08,
            Position.Midfielder => 1.03,
            Position.Defender => 0.96,
            Position.Goalkeeper => 0.82,
            _ => 1.0
        };
    }

    private static double GetReputationModifier(Player player)
    {
        return player.OverallRating switch
        {
            >= 94 => 1.12,
            >= 91 => 1.08,
            >= 88 => 1.04,
            >= 84 => 1.02,
            _ => 1.0
        };
    }

    private static double GetLeagueModifier(string leagueId)
    {
        return LeagueReputation.TryGetValue(leagueId, out var modifier) ? modifier : 1.0;
    }

    private static decimal GetLeagueAskingModifier(string leagueId)
    {
        return leagueId switch
        {
            "premier-league" => 0.04m,
            "la-liga" => 0.03m,
            "serie-a" or "bundesliga" => 0.02m,
            "ligue-1" => 0.01m,
            _ => 0m
        };
    }

    private static decimal GetContractAskingAdjustment(Player player)
    {
        if (player.ContractEndYear is null)
        {
            return 0m;
        }

        return PlayerContractService.GetYearsRemaining(player) switch
        {
            0 => -0.18m,
            1 => -0.10m,
            2 => 0m,
            3 => 0.03m,
            >= 4 => 0.08m,
            _ => 0m
        };
    }

    private static double GetPerformanceModifier(Player player, IEnumerable<PlayerSeasonStats>? seasonStats)
    {
        var stats = seasonStats?.FirstOrDefault(stat => stat.PlayerName == player.Name);
        if (stats is null || stats.Appearances == 0)
        {
            return 1.0;
        }

        return stats.AverageRating switch
        {
            >= 7.6 => 1.07,
            >= 7.1 => 1.035,
            <= 6.2 => 0.94,
            _ => 1.0
        };
    }

    private static double GetAvailabilityModifier(Player player)
    {
        if (player.IsSeasonEndingInjury)
        {
            return 0.55;
        }

        return player.IsInjured || player.InjuryRecoveryMatches > 0 ? 0.78 : 1.0;
    }

    private static decimal RoundToMarketFigure(decimal value)
    {
        var step = value >= 20_000_000 ? 500_000 : 100_000;
        return Math.Round(value / step, MidpointRounding.AwayFromZero) * step;
    }

    private static decimal GetValueCap(Player player)
    {
        return player.OverallRating switch
        {
            >= 95 => 250_000_000m,
            >= 92 => 225_000_000m,
            >= 89 => 155_000_000m,
            >= 88 => 135_000_000m,
            >= 85 => player.Age is <= 22 ? 140_000_000m : 105_000_000m,
            >= 80 => 60_000_000m,
            >= 75 => 25_000_000m,
            _ => 45_000_000m
        };
    }

    private static decimal GetAskingPriceCap(Player player)
    {
        return player.OverallRating switch
        {
            >= 95 => 320_000_000m,
            >= 92 => 300_000_000m,
            >= 89 => 210_000_000m,
            >= 88 => 180_000_000m,
            >= 85 => player.Age is <= 22 ? 175_000_000m : 140_000_000m,
            >= 80 => 85_000_000m,
            >= 75 => 45_000_000m,
            _ => 60_000_000m
        };
    }

    private static decimal GetMaximumAskingMultiplier(Player player)
    {
        if (player.TransferStatus == PlayerTransferStatus.Listed)
        {
            return 1.05m;
        }

        if (player.Age >= 31 || player.FormStatus is PlayerFormStatus.Poor or PlayerFormStatus.VeryPoor)
        {
            return player.Role == PlayerRole.KeyPlayer && player.OverallRating >= 88 ? 1.20m : 0.95m;
        }

        if (player.RejectTransferOffers || player.TransferStatus == PlayerTransferStatus.Unavailable)
        {
            return 1.60m;
        }

        if (player.OverallRating >= 90 || player.Role == PlayerRole.KeyPlayer && player.OverallRating >= 88)
        {
            return 1.60m;
        }

        if (player.Age <= 23 && (player.PotentialOverall ?? player.OverallRating) - player.OverallRating >= 4)
        {
            return 1.35m;
        }

        return player.OverallRating switch
        {
            >= 85 => 1.35m,
            _ => 1.20m
        };
    }

    private static decimal GetMinimumAskingMultiplier(Player player)
    {
        if (player.Age >= 31 || player.FormStatus is PlayerFormStatus.Poor or PlayerFormStatus.VeryPoor)
        {
            return 0.70m;
        }

        return player.TransferStatus == PlayerTransferStatus.Listed ? 0.85m : 1.0m;
    }
}
