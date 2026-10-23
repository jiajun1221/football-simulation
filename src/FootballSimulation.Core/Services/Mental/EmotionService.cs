using FootballSimulation.Engine;
using FootballSimulation.Models;

namespace FootballSimulation.Services;

public sealed class EmotionService
{
    private readonly IReadOnlyDictionary<string, Player>? _playersByName;

    public EmotionService()
    {
    }

    public EmotionService(Match match)
    {
        _playersByName = match.HomeTeam.AllPlayers.Concat(match.AwayTeam.AllPlayers)
            .GroupBy(player => player.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    public void ProcessEvent(Match match, MatchEvent matchEvent)
    {
        foreach (var (player, isPrimary) in ResolvePlayers(match, matchEvent, _playersByName))
        {
            DecayToBaseline(player, matchEvent.Minute);
            var impact = GetImpact(matchEvent.EventType, isPrimary);
            if (impact == 0) continue;

            var p = player.MentalState.Personality;
            var multiplier = impact < 0
                ? 1.15 - p.Resilience / 50.0
                : 0.85 + p.Resilience / 50.0;
            if (matchEvent.EventType == EventType.Substitution && isPrimary && impact < 0)
            {
                multiplier *= 0.75 + (p.Ambition + p.Ego) / 30.0;
            }
            var effects = PersonalityService.GetArchetypeEffects(player);
            multiplier *= effects.GeneralEventResponse;
            if (impact < 0)
            {
                multiplier *= effects.NegativeEventResponse;
                if (match.IsRivalryMatch) multiplier *= effects.RivalryNegativeResponse;
                if (matchEvent.EventType == EventType.Substitution && isPrimary)
                    multiplier *= effects.SubstitutionNegativeResponse;
            }
            else if (matchEvent.Minute >= 75 || match.IsRivalryMatch)
            {
                multiplier *= effects.PositiveLateOrRivalryResponse;
            }
            var context = GetContextModifier(match, matchEvent, player);
            Apply(player, impact * multiplier * context, matchEvent, effects);
        }
    }

    public static double GetActionModifier(Player player, MatchActionType actionType)
    {
        var raw = player.MentalState.MatchEmotionModifier;
        if (actionType is MatchActionType.Passing or MatchActionType.FirstTouch or MatchActionType.ChanceCreation)
        {
            raw += player.MentalState.MatchChemistryModifier;
        }
        return Math.Clamp(raw, -MentalSystemConstants.MaximumGeneralActionModifier, MentalSystemConstants.MaximumGeneralActionModifier);
    }

    public static double GetFoulRiskModifier(Player player)
    {
        var p = player.MentalState.Personality;
        var e = player.MentalState.Emotions;
        var effects = PersonalityService.GetArchetypeEffects(player);
        var raw = (e.Frustration - 35) / 700.0 + (10 - p.Temperament) / 250.0 +
            (10 - p.Sportsmanship) / 300.0 + effects.FoulRiskAdjustment;
        return 1.0 + Math.Clamp(raw, -MentalSystemConstants.MaximumFoulAndCardModifier, MentalSystemConstants.MaximumFoulAndCardModifier);
    }

    public static void DecayAfterCalendarSlot(Player player, int calendarRound)
    {
        var e = player.MentalState.Emotions;
        var p = player.MentalState.Personality;
        var effects = PersonalityService.GetArchetypeEffects(player);
        var recovery = (0.12 + p.Professionalism / 100.0) * effects.RecoveryRate;
        e.Confidence = MoveToward(e.Confidence, Math.Clamp(player.Morale, 40, 60), recovery * effects.ConfidenceRecoveryRate);
        e.Frustration = MoveToward(e.Frustration, Math.Clamp(30 - p.Temperament * 0.7, 10, 30), recovery);
        e.Anxiety = MoveToward(e.Anxiety, Math.Clamp(30 - p.Resilience * 0.7 + effects.AnxietyBaselineAdjustment, 10, 33), recovery);
        e.Joy = MoveToward(e.Joy, 40, recovery);
        e.Focus = MoveToward(e.Focus, 50 + p.Professionalism * 0.5 + effects.FocusBaselineAdjustment, recovery);
        e.LastUpdatedCalendarRound = calendarRound;
    }

    public static void ApplyBenchedReaction(Player player, int calendarRound)
    {
        var adjustment = PersonalityService.GetArchetypeEffects(player).BenchedFrustrationAdjustment;
        if (adjustment <= 0) return;

        player.MentalState.Emotions.Frustration = Math.Clamp(
            player.MentalState.Emotions.Frustration + adjustment, 0, 100);
        player.MentalState.RecentChanges.Add(new MentalChangeRecord
        {
            CalendarRound = calendarRound,
            Category = "Emotion",
            Delta = adjustment,
            Reason = "Benched"
        });
        if (player.MentalState.RecentChanges.Count > MentalSystemConstants.MaximumRecentChanges)
            player.MentalState.RecentChanges.RemoveRange(0, player.MentalState.RecentChanges.Count - MentalSystemConstants.MaximumRecentChanges);
    }

    private static IEnumerable<(Player Player, bool IsPrimary)> ResolvePlayers(
        Match match,
        MatchEvent matchEvent,
        IReadOnlyDictionary<string, Player>? playersByName)
    {
        var all = playersByName is null ? match.HomeTeam.AllPlayers.Concat(match.AwayTeam.AllPlayers) : null;
        var primary = matchEvent.PrimaryPlayerName is not null && playersByName is not null
            ? playersByName.GetValueOrDefault(matchEvent.PrimaryPlayerName)
            : all?.FirstOrDefault(p => p.Name.Equals(matchEvent.PrimaryPlayerName, StringComparison.OrdinalIgnoreCase));
        if (primary is not null) yield return (primary, true);
        var secondary = matchEvent.SecondaryPlayerName is not null && playersByName is not null
            ? playersByName.GetValueOrDefault(matchEvent.SecondaryPlayerName)
            : all?.FirstOrDefault(p => p.Name.Equals(matchEvent.SecondaryPlayerName, StringComparison.OrdinalIgnoreCase));
        if (secondary is not null && !ReferenceEquals(primary, secondary)) yield return (secondary, false);
    }

    private static double GetImpact(EventType type, bool primary) => type switch
    {
        EventType.Goal or EventType.WonderGoal => primary ? 8 : 3,
        EventType.Save or EventType.GoalkeeperHeroics => primary ? 4 : 2,
        EventType.Miss or EventType.BadPass or EventType.Miscontrol => primary ? -3 : 0,
        EventType.DefensiveError or EventType.GoalkeeperMistake => primary ? -7 : 0,
        EventType.YellowCard => primary ? -2 : 0,
        EventType.RedCard => primary ? -8 : 0,
        EventType.Foul => primary ? -2 : 1,
        EventType.Injury => primary ? -5 : 0,
        EventType.Substitution => primary ? -1 : 1,
        _ => 0
    };

    private static double GetContextModifier(Match match, MatchEvent matchEvent, Player player)
    {
        var late = matchEvent.Minute >= 75 ? 1.15 : 1.0;
        var rivalry = match.IsRivalryMatch ? 1.15 : 1.0;
        var bigMatch = player.MentalState.Personality.BigMatchTemperament;
        var pressure = (late * rivalry) * (1.1 - (bigMatch - 10) / 100.0);
        return Math.Clamp(pressure, 0.75, 1.35);
    }

    private static void Apply(Player player, double impact, MatchEvent matchEvent, ArchetypeEffects effects)
    {
        var e = player.MentalState.Emotions;
        e.Confidence = Math.Clamp(e.Confidence + impact, 0, 100);
        e.Frustration = Math.Clamp(e.Frustration - impact * 0.7 * effects.FrustrationResponse, 0, 100);
        e.Anxiety = Math.Clamp(e.Anxiety - impact * 0.4, 0, 100);
        e.Joy = Math.Clamp(e.Joy + impact * 0.8 * effects.JoyResponse, 0, 100);
        e.Focus = Math.Clamp(e.Focus + (impact > 0 ? -Math.Min(3, impact * 0.25) : impact * 0.35), 0, 100);
        e.LastUpdatedMinute = matchEvent.Minute;
        RefreshMatchModifier(player);
        AddReason(player, impact, matchEvent);
    }

    private static void DecayToBaseline(Player player, int minute)
    {
        var e = player.MentalState.Emotions;
        var elapsed = Math.Max(0, minute - e.LastUpdatedMinute);
        if (elapsed == 0) return;
        var rate = Math.Min(0.30, elapsed * 0.012);
        e.Confidence = MoveToward(e.Confidence, player.Morale, rate);
        e.Frustration = MoveToward(e.Frustration, 20, rate);
        e.Anxiety = MoveToward(e.Anxiety, 20, rate);
        e.Joy = MoveToward(e.Joy, 40, rate);
        e.Focus = MoveToward(e.Focus, 55, rate);
        e.LastUpdatedMinute = minute;
    }

    private static double MoveToward(double value, double target, double fraction) => value + (target - value) * fraction;

    public static void RefreshMatchModifier(Player player)
    {
        var e = player.MentalState.Emotions;
        player.MentalState.MatchEmotionModifier = Math.Clamp(
            ((e.Confidence - 50) * 0.00035) +
            ((e.Focus - 50) * 0.00045) -
            (Math.Max(0, e.Anxiety - 35) * 0.00035) -
            (Math.Max(0, e.Frustration - 40) * 0.00025),
            -MentalSystemConstants.MaximumGeneralActionModifier,
            MentalSystemConstants.MaximumGeneralActionModifier);
    }

    private static void AddReason(Player player, double delta, MatchEvent matchEvent)
    {
        player.MentalState.RecentChanges.Add(new MentalChangeRecord
        {
            MatchMinute = matchEvent.Minute,
            Category = "Emotion",
            Delta = Math.Round(delta, 2),
            Reason = matchEvent.EventType.ToString()
        });
        if (player.MentalState.RecentChanges.Count > MentalSystemConstants.MaximumRecentChanges)
            player.MentalState.RecentChanges.RemoveRange(0, player.MentalState.RecentChanges.Count - MentalSystemConstants.MaximumRecentChanges);
    }
}
