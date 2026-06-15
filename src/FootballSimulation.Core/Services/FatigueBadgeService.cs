using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class FatigueBadgeService
{
    private const double FullStaminaThreshold = 99.5;

    public static FatigueBadgeResult Evaluate(Player player, int? fixtureGapDays = null)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (player.IsInjured || player.IsSuspended || player.IsSentOff)
        {
            return FatigueBadgeResult.None;
        }

        var stamina = Math.Clamp((int)Math.Round(player.Stamina), 0, 100);
        var effectiveRecentLoad = GetEffectiveRecentLoad(player);
        var hasFullStamina = player.Stamina >= FullStaminaThreshold;
        var isShortRest = fixtureGapDays is <= 3;
        var isLessThanFourDaysRest = fixtureGapDays is < 4;
        var isLongRest = fixtureGapDays is >= 5;

        if (hasFullStamina)
        {
            if (player.SeasonFatigue >= 90)
            {
                return CreateRisk("Season fatigue 90+");
            }

            if (player.ConsecutiveStarts >= 10)
            {
                return CreateLoad($"Started {player.ConsecutiveStarts} consecutive matches");
            }

            return FatigueBadgeResult.None;
        }

        if (stamina < 50)
        {
            return CreateRisk($"Stamina {stamina}%");
        }

        if (player.SeasonFatigue >= 85)
        {
            return CreateRisk("Season fatigue 85+");
        }

        if (stamina < 60 && (effectiveRecentLoad >= 7 || player.SeasonFatigue >= 65))
        {
            return CreateRisk($"Stamina {stamina}% with high workload");
        }

        if (player.ConsecutiveFullMatches >= 3 && isLessThanFourDaysRest)
        {
            return CreateRisk("Played 90 minutes in 3 straight matches with short rest");
        }

        var loadReason = GetLoadReason(player);
        if (!string.IsNullOrWhiteSpace(loadReason))
        {
            if (stamina >= 90 && player.SeasonFatigue < 60)
            {
                return FatigueBadgeResult.None;
            }

            return CreateLoad(loadReason);
        }

        if (isLongRest)
        {
            return stamina < 65
                ? CreateTired($"Stamina {stamina}%")
                : FatigueBadgeResult.None;
        }

        if (stamina < 65)
        {
            return CreateTired($"Stamina {stamina}%");
        }

        if (stamina < 75 && player.SeasonFatigue >= 65)
        {
            return CreateTired($"Stamina {stamina}% with season fatigue {player.SeasonFatigue}");
        }

        if (stamina < 80 && effectiveRecentLoad >= 6)
        {
            return CreateTired($"Stamina {stamina}% with high recent workload");
        }

        if (player.ConsecutiveFullMatches >= 2 && isShortRest)
        {
            return CreateTired("Played 90 minutes in last 2 matches with short rest");
        }

        return FatigueBadgeResult.None;
    }

    private static int GetEffectiveRecentLoad(Player player)
    {
        var recentLoad = Math.Max(0, player.MatchesPlayedRecently);
        if (player.RecentMatchMinutes.Count == 0)
        {
            return recentLoad;
        }

        return player.LastMatchMinutes switch
        {
            0 => Math.Max(0, recentLoad - 2),
            < 30 => Math.Max(0, recentLoad - 1),
            _ => recentLoad
        };
    }

    private static string GetLoadReason(Player player)
    {
        if (player.ConsecutiveStarts >= 8)
        {
            return $"Started {player.ConsecutiveStarts} consecutive matches";
        }

        if (player.MatchesPlayedRecently >= 8)
        {
            return $"Recent match load {player.MatchesPlayedRecently}";
        }

        if (player.MinutesInLastFiveMatches >= 400)
        {
            return $"{player.MinutesInLastFiveMatches} minutes in last 5 matches";
        }

        return string.Empty;
    }

    private static FatigueBadgeResult CreateRisk(string reason)
    {
        return new FatigueBadgeResult(
            "Risk",
            string.Join(Environment.NewLine, reason, "Increased injury risk"),
            "#DC2626");
    }

    private static FatigueBadgeResult CreateLoad(string reason)
    {
        return new FatigueBadgeResult(
            "Load",
            reason,
            "#F97316");
    }

    private static FatigueBadgeResult CreateTired(string reason)
    {
        return new FatigueBadgeResult(
            "Tired",
            string.Join(Environment.NewLine, reason, "High recent workload"),
            "#F59E0B");
    }
}

public sealed record FatigueBadgeResult(string Text, string Tooltip, string Background)
{
    public static FatigueBadgeResult None { get; } = new(string.Empty, string.Empty, "#F59E0B");
}
