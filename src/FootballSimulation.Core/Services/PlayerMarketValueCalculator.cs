using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class PlayerMarketValueCalculator
{
    private static readonly Dictionary<string, double> LeagueReputation = new(StringComparer.OrdinalIgnoreCase)
    {
        ["premier-league"] = 1.18,
        ["la-liga"] = 1.12,
        ["serie-a"] = 1.05,
        ["bundesliga"] = 1.08,
        ["ligue-1"] = 1.0
    };

    public decimal CalculateMarketValue(Player player, string leagueId, IEnumerable<PlayerSeasonStats>? seasonStats = null)
    {
        ArgumentNullException.ThrowIfNull(player);

        var overall = Math.Clamp(player.OverallRating, 45, 99);
        var baseValue = GetBaseValueFromOverall(overall);
        var value = (double)baseValue *
            GetAgeModifier(player) *
            GetFormModifier(player) *
            GetPotentialModifier(player) *
            GetPositionModifier(player) *
            GetReputationModifier(player) *
            GetLeagueModifier(leagueId) *
            GetPerformanceModifier(player, seasonStats) *
            GetAvailabilityModifier(player);

        var cappedValue = Math.Min((decimal)value, GetValueCap(overall));
        return RoundToMarketFigure(Math.Max(250_000, cappedValue));
    }

    public decimal CalculateAskingPrice(Player player, string leagueId, IEnumerable<PlayerSeasonStats>? seasonStats = null)
    {
        var marketValue = CalculateMarketValue(player, leagueId, seasonStats);
        var roleModifier = player.Role switch
        {
            PlayerRole.KeyPlayer => 1.45m,
            PlayerRole.Starter => 1.2m,
            PlayerRole.Prospect => 1.25m,
            PlayerRole.Backup => 0.9m,
            _ => 1.0m
        };

        if (player.TransferStatus == PlayerTransferStatus.Listed)
        {
            roleModifier -= 0.15m;
        }

        return RoundToMarketFigure(Math.Min(marketValue * Math.Max(0.85m, roleModifier), GetAskingPriceCap(player.OverallRating)));
    }

    private static decimal GetBaseValueFromOverall(int overall)
    {
        return overall switch
        {
            >= 95 => 230_000_000m,
            >= 94 => 215_000_000m,
            >= 93 => 195_000_000m,
            >= 92 => 175_000_000m,
            >= 91 => 155_000_000m,
            >= 90 => 135_000_000m,
            >= 89 => 115_000_000m,
            >= 88 => 98_000_000m,
            >= 87 => 84_000_000m,
            >= 86 => 72_000_000m,
            >= 85 => 62_000_000m,
            >= 84 => 52_000_000m,
            >= 83 => 44_000_000m,
            >= 82 => 36_000_000m,
            >= 81 => 30_000_000m,
            >= 80 => 25_000_000m,
            >= 78 => 18_000_000m,
            >= 76 => 13_000_000m,
            >= 74 => 9_000_000m,
            >= 72 => 6_000_000m,
            >= 70 => 4_000_000m,
            >= 65 => 1_800_000m,
            >= 60 => 850_000m,
            _ => 350_000m
        };
    }

    private static double GetAgeModifier(Player player)
    {
        var age = player.Age ?? 26;
        if (age <= 19) return 1.25;
        if (age <= 22) return 1.35;
        if (age <= 25) return 1.18;
        if (age <= 29) return 1.0;
        if (age <= 32) return 0.78;
        if (age <= 35) return 0.52;
        return 0.32;
    }

    private static double GetFormModifier(Player player)
    {
        return player.FormStatus switch
        {
            PlayerFormStatus.Excellent => 1.18,
            PlayerFormStatus.Good => 1.08,
            PlayerFormStatus.Poor => 0.9,
            PlayerFormStatus.VeryPoor => 0.78,
            _ => 1.0
        };
    }

    private static double GetPotentialModifier(Player player)
    {
        var potential = player.PotentialOverall ?? player.OverallRating;
        var gap = Math.Clamp(potential - player.OverallRating, 0, 12);
        return 1.0 + gap * 0.035;
    }

    private static double GetPositionModifier(Player player)
    {
        return player.Position switch
        {
            Position.Forward => 1.18,
            Position.Midfielder => 1.08,
            Position.Defender => 0.98,
            Position.Goalkeeper => 0.86,
            _ => 1.0
        };
    }

    private static double GetReputationModifier(Player player)
    {
        return player.OverallRating switch
        {
            >= 91 => 1.3,
            >= 88 => 1.18,
            >= 84 => 1.08,
            _ => 1.0
        };
    }

    private static double GetLeagueModifier(string leagueId)
    {
        return LeagueReputation.TryGetValue(leagueId, out var modifier) ? modifier : 1.0;
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
            >= 7.6 => 1.12,
            >= 7.1 => 1.06,
            <= 6.2 => 0.92,
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

    private static decimal GetValueCap(int overall)
    {
        return overall switch
        {
            >= 92 => 250_000_000m,
            >= 90 => 220_000_000m,
            >= 88 => 185_000_000m,
            >= 86 => 150_000_000m,
            >= 84 => 115_000_000m,
            >= 82 => 85_000_000m,
            >= 80 => 65_000_000m,
            _ => 45_000_000m
        };
    }

    private static decimal GetAskingPriceCap(int overall)
    {
        return overall switch
        {
            >= 92 => 320_000_000m,
            >= 90 => 285_000_000m,
            >= 88 => 235_000_000m,
            >= 86 => 190_000_000m,
            >= 84 => 150_000_000m,
            >= 82 => 115_000_000m,
            >= 80 => 85_000_000m,
            _ => 60_000_000m
        };
    }
}
