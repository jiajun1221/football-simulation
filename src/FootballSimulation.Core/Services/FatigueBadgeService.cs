using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class FatigueBadgeService
{
    private const double FullStaminaThreshold = 99.5;
    private const int TiredWorkloadRiskThreshold = 55;
    private const int RiskWorkloadRiskThreshold = 70;
    private const double GoalkeeperStaminaRiskMultiplier = 0.65;
    private const double GoalkeeperWorkloadRiskMultiplier = 0.40;
    private const double GoalkeeperSeasonFatigueRiskMultiplier = 0.70;

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
        var workloadRisk = CalculateWorkloadRiskPercentage(player, fixtureGapDays);

        if (hasFullStamina)
        {
            if (player.SeasonFatigue >= 92)
            {
                return CreateTired($"Full stamina with season fatigue {player.SeasonFatigue}");
            }

            if (player.ConsecutiveStarts >= 12)
            {
                return CreateLoad($"Started {player.ConsecutiveStarts} consecutive matches");
            }

            return FatigueBadgeResult.None;
        }

        if (stamina < 50)
        {
            return CreateRisk($"Stamina {stamina}%");
        }

        if (stamina < 55 && (effectiveRecentLoad >= 6 || player.SeasonFatigue >= 70))
        {
            return CreateRisk($"Stamina {stamina}% with high workload");
        }

        if (player.SeasonFatigue >= 90)
        {
            return stamina < 75
                ? CreateRisk("Season fatigue 90+")
                : CreateTired($"Stamina {stamina}% with season fatigue {player.SeasonFatigue}");
        }

        if (player.ConsecutiveFullMatches >= 4 && isLessThanFourDaysRest)
        {
            return CreateShortRestFullMatchBadge(
                "Played 90 minutes in 4 straight matches with short rest",
                workloadRisk);
        }

        var loadReason = GetLoadReason(player, effectiveRecentLoad);
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

        if (stamina < 60)
        {
            return CreateTired($"Stamina {stamina}%");
        }

        if (stamina < 70 && player.SeasonFatigue >= 75)
        {
            return CreateTired($"Stamina {stamina}% with season fatigue {player.SeasonFatigue}");
        }

        if (stamina < 76 && effectiveRecentLoad >= 6)
        {
            return CreateTired($"Stamina {stamina}% with high recent workload");
        }

        if (player.ConsecutiveFullMatches >= 3 && isShortRest)
        {
            return CreateShortRestFullMatchBadge(
                "Played 90 minutes in last 3 matches with short rest",
                workloadRisk);
        }

        return FatigueBadgeResult.None;
    }

    public static int CalculateWorkloadRiskPercentage(Player player, int? fixtureGapDays = null)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (player.IsInjured)
        {
            return 100;
        }

        if (player.IsSuspended || player.IsSentOff)
        {
            return 0;
        }

        var stamina = Math.Clamp((int)Math.Round(player.Stamina), 0, 100);
        var effectiveRecentLoad = GetEffectiveRecentLoad(player);
        var risk = 0.0;

        var isGoalkeeper = PositionSuitabilityService.IsGoalkeeperCapable(player);
        var staminaRiskMultiplier = isGoalkeeper ? GoalkeeperStaminaRiskMultiplier : 1.0;
        var workloadRiskMultiplier = isGoalkeeper ? GoalkeeperWorkloadRiskMultiplier : 1.0;
        var seasonFatigueRiskMultiplier = isGoalkeeper ? GoalkeeperSeasonFatigueRiskMultiplier : 1.0;

        risk += Math.Max(0, 100 - stamina) * 0.9 * staminaRiskMultiplier;
        risk += Math.Clamp(player.SeasonFatigue, 0, 100) * 0.32 * seasonFatigueRiskMultiplier;
        risk += Math.Min(35, effectiveRecentLoad * 4.5) * workloadRiskMultiplier;
        risk += Math.Min(18, Math.Max(0, player.ConsecutiveStarts - 5) * 2.5) * workloadRiskMultiplier;
        risk += Math.Min(18, Math.Max(0, player.MinutesInLastFiveMatches - 300) * 0.12) * workloadRiskMultiplier;

        if (player.ConsecutiveFullMatches >= 3)
        {
            risk += Math.Min(30, (player.ConsecutiveFullMatches - 2) * 10) * workloadRiskMultiplier;
        }

        if (player.ConsecutiveFullMatches >= 4 && fixtureGapDays is < 4)
        {
            risk += 25 * workloadRiskMultiplier;
        }

        if (fixtureGapDays is <= 3)
        {
            var lastMatchMinutes = player.RecentMatchMinutes.Count > 0
                ? player.LastMatchMinutes
                : player.MatchesPlayedRecently > 0 ? 90 : 0;
            var shortRestExposure = Math.Clamp(lastMatchMinutes / 90.0, 0.0, 1.0);
            risk += (isGoalkeeper ? 5 : 10) * shortRestExposure;
        }
        else if (fixtureGapDays is >= 5)
        {
            risk -= 10;
        }

        if (stamina >= 95 && player.SeasonFatigue < 45 && effectiveRecentLoad < 6)
        {
            risk *= isGoalkeeper ? 0.30 : 0.45;
        }

        return Math.Clamp(
            ApplyGoalkeeperRiskCap(player, risk, stamina),
            0,
            100);
    }

    private static int GetEffectiveRecentLoad(Player player)
    {
        var recentLoad = Math.Max(0, player.MatchesPlayedRecently);
        if (player.RecentMatchMinutes.Count == 0)
        {
            return recentLoad;
        }

        var reducedLoad = recentLoad;
        foreach (var minutes in player.RecentMatchMinutes.AsEnumerable().Reverse())
        {
            if (minutes == 0)
            {
                reducedLoad -= 2;
                continue;
            }

            if (minutes < 30)
            {
                reducedLoad -= 1;
            }

            break;
        }

        return Math.Max(0, reducedLoad);
    }

    private static int ApplyGoalkeeperRiskCap(Player player, double risk, int stamina)
    {
        var roundedRisk = (int)Math.Round(risk);
        if (!PositionSuitabilityService.IsGoalkeeperCapable(player) ||
            player.Traits.Contains(PlayerTrait.InjuryProne))
        {
            return roundedRisk;
        }

        if (stamina >= 95 && player.SeasonFatigue <= 35)
        {
            return Math.Min(roundedRisk, 25);
        }

        if (stamina >= 90 && player.SeasonFatigue <= 50)
        {
            return Math.Min(roundedRisk, 35);
        }

        if (stamina >= 85 && player.SeasonFatigue <= 65)
        {
            return Math.Min(roundedRisk, 45);
        }

        if (stamina >= 80 && player.SeasonFatigue <= 75)
        {
            return Math.Min(roundedRisk, 55);
        }

        return roundedRisk;
    }

    private static string GetLoadReason(Player player, int effectiveRecentLoad)
    {
        var isGoalkeeper = PositionSuitabilityService.IsGoalkeeperCapable(player);
        var consecutiveStartsThreshold = isGoalkeeper ? 14 : 10;
        var recentLoadThreshold = isGoalkeeper ? 9 : 7;
        var recentMinutesThreshold = isGoalkeeper ? 470 : 430;

        if (player.ConsecutiveStarts >= consecutiveStartsThreshold)
        {
            return $"Started {player.ConsecutiveStarts} consecutive matches";
        }

        if (effectiveRecentLoad >= recentLoadThreshold)
        {
            return $"Recent match load {effectiveRecentLoad}";
        }

        if (player.MinutesInLastFiveMatches >= recentMinutesThreshold)
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

    private static FatigueBadgeResult CreateShortRestFullMatchBadge(string reason, int workloadRisk)
    {
        return workloadRisk switch
        {
            >= RiskWorkloadRiskThreshold => CreateRisk(reason),
            >= TiredWorkloadRiskThreshold => CreateTired(reason),
            _ => FatigueBadgeResult.None
        };
    }
}

public sealed record FatigueBadgeResult(string Text, string Tooltip, string Background)
{
    public static FatigueBadgeResult None { get; } = new(string.Empty, string.Empty, "#F59E0B");
}
