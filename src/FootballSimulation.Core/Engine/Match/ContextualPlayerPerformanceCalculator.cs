using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Engine;

public enum MatchActionType
{
    Passing,
    FirstTouch,
    Dribbling,
    Tackling,
    Interception,
    AerialDuel,
    ChanceCreation,
    Finishing,
    Goalkeeping,
    BallControl,
    PhysicalDuel,
    Pressing,
    Crossing,
    RecoveryDefending,
    CrossClaiming
}

public sealed record ContextualActionResult(
    double Score,
    IReadOnlyList<PlayerTrait> AppliedTraits);

/// <summary>
/// Produces a comparable 1-99 action score. Overall quality anchors the result,
/// while the attributes that matter for the action remain the largest component.
/// </summary>
public sealed class ContextualPlayerPerformanceCalculator
{
    public ContextualActionResult Calculate(Player player, MatchActionType actionType)
    {
        ArgumentNullException.ThrowIfNull(player);

        var attributes = PlayerAttributeService.GetAttributes(player);
        var actionRating = GetActionRating(player, attributes, actionType);
        var staminaAndPosition = GetStaminaAndPositionScore(player);
        var currentCondition = GetCurrentConditionScore(player);
        var appliedTraits = GetRelevantTraits(player, actionType).Take(2).ToList();

        var score =
            GetOverallRating(player) * 0.35 +
            actionRating * 0.45 +
            staminaAndPosition * 0.10 +
            currentCondition * 0.10 +
            appliedTraits.Sum(trait => GetTraitBonus(trait, actionType));

        // Stamina is part of the normal weighted score, but exhaustion must remain
        // a genuine late-match collapse rather than a small linear penalty.
        score *= GetExhaustionModifier(player.Stamina);

        if (player.IsInjured || player.IsSuspended || player.IsSentOff)
        {
            score *= 0.35;
        }

        return new ContextualActionResult(Math.Clamp(score, 1, 99), appliedTraits);
    }

    public double GetContestProbability(
        Player actor,
        MatchActionType actorAction,
        Player opponent,
        MatchActionType opponentAction,
        double tacticalShift = 0)
    {
        var actorScore = Calculate(actor, actorAction).Score;
        var opponentScore = Calculate(opponent, opponentAction).Score;
        var qualityShift = Math.Clamp((actorScore - opponentScore) * 0.012, -0.30, 0.30);
        return Math.Clamp(0.50 + qualityShift + Math.Clamp(tacticalShift, -0.10, 0.10), 0.12, 0.88);
    }

    private static double GetOverallRating(Player player)
    {
        var fallback = new[] { player.Attack, player.Defense, player.Passing, player.Finishing }
            .Where(value => value > 0)
            .DefaultIfEmpty(50)
            .Average();
        return Math.Clamp(player.OverallRating > 0 ? player.OverallRating : fallback, 1, 99);
    }

    private static double GetActionRating(Player player, PlayerAttributeRatings attributes, MatchActionType actionType)
    {
        return actionType switch
        {
            MatchActionType.Passing => player.Passing * 0.60 + attributes.Passing * 0.30 + attributes.Dribbling * 0.10,
            MatchActionType.FirstTouch => attributes.Dribbling * 0.55 + attributes.Passing * 0.30 + attributes.Physical * 0.15,
            MatchActionType.Dribbling => attributes.Dribbling * 0.58 + attributes.Pace * 0.27 + player.Attack * 0.15,
            MatchActionType.Tackling => attributes.Defending * 0.62 + attributes.Physical * 0.23 + player.Defense * 0.15,
            MatchActionType.Interception => attributes.Defending * 0.58 + attributes.Pace * 0.17 + player.Defense * 0.25,
            MatchActionType.AerialDuel => attributes.Physical * 0.55 + attributes.Defending * 0.25 + GetOverallRating(player) * 0.20,
            MatchActionType.ChanceCreation => attributes.Passing * 0.48 + attributes.Dribbling * 0.27 + player.Passing * 0.25,
            MatchActionType.Finishing => player.Finishing * 0.50 + attributes.Shooting * 0.38 + attributes.Physical * 0.12,
            MatchActionType.Goalkeeping => attributes.Defending * 0.62 + attributes.Physical * 0.18 + GetOverallRating(player) * 0.20,
            MatchActionType.BallControl => attributes.Dribbling * 0.50 + attributes.Passing * 0.25 + attributes.Physical * 0.25,
            MatchActionType.PhysicalDuel => attributes.Physical * 0.62 + attributes.Defending * 0.20 + GetOverallRating(player) * 0.18,
            MatchActionType.Pressing => attributes.Defending * 0.40 + attributes.Physical * 0.30 + attributes.Pace * 0.30,
            MatchActionType.Crossing => attributes.Passing * 0.58 + attributes.Dribbling * 0.22 + player.Passing * 0.20,
            MatchActionType.RecoveryDefending => attributes.Pace * 0.48 + attributes.Defending * 0.37 + attributes.Physical * 0.15,
            MatchActionType.CrossClaiming => attributes.Defending * 0.48 + attributes.Physical * 0.32 + GetOverallRating(player) * 0.20,
            _ => GetOverallRating(player)
        };
    }

    private static double GetStaminaAndPositionScore(Player player)
    {
        var stamina = Math.Clamp(player.Stamina, 0, 100);
        var suitability = PositionSuitabilityService.GetEffectivenessMultiplier(player) * 100;
        return stamina * 0.60 + suitability * 0.40;
    }

    private static double GetCurrentConditionScore(Player player)
    {
        var form = Math.Clamp(player.CurrentForm, 0, 100);
        var morale = Math.Clamp(player.Morale, 0, 100);
        var liveConfidence = Math.Clamp(player.LiveMatchModifier, 0.75, 1.15) * 100;
        return form * 0.40 + morale * 0.30 + liveConfidence * 0.30;
    }

    private static double GetExhaustionModifier(double stamina)
    {
        return stamina switch
        {
            < 15 => 0.42,
            < 30 => 0.52,
            < 50 => 0.72,
            < 65 => 0.88,
            _ => 1.0
        };
    }

    private static IEnumerable<PlayerTrait> GetRelevantTraits(Player player, MatchActionType actionType)
    {
        var relevant = actionType switch
        {
            MatchActionType.Passing => new[] { PlayerTrait.Playmaker, PlayerTrait.LongPasser, PlayerTrait.TeamPlayer, PlayerTrait.PressResistant, PlayerTrait.Composed },
            MatchActionType.FirstTouch => new[] { PlayerTrait.FirstTouch, PlayerTrait.PressResistant, PlayerTrait.TechnicalDribbler, PlayerTrait.TeamPlayer },
            MatchActionType.Dribbling => new[] { PlayerTrait.TechnicalDribbler, PlayerTrait.SpeedDribbler, PlayerTrait.Rapid, PlayerTrait.Flair },
            MatchActionType.Tackling => new[] { PlayerTrait.BallWinner, PlayerTrait.DivesIntoTackles, PlayerTrait.Interceptor, PlayerTrait.Engine },
            MatchActionType.Interception => new[] { PlayerTrait.Interceptor, PlayerTrait.BallWinner, PlayerTrait.Engine, PlayerTrait.BoxToBox },
            MatchActionType.AerialDuel => new[] { PlayerTrait.Strong, PlayerTrait.AerialThreat, PlayerTrait.PowerHeader, PlayerTrait.TargetForward },
            MatchActionType.ChanceCreation => new[] { PlayerTrait.Playmaker, PlayerTrait.CrossingSpecialist, PlayerTrait.LongPasser, PlayerTrait.TeamPlayer },
            MatchActionType.Finishing => new[] { PlayerTrait.ClinicalFinisher, PlayerTrait.Composed, PlayerTrait.Poacher, PlayerTrait.Acrobatics, PlayerTrait.FinesseShot, PlayerTrait.LongShotTaker, PlayerTrait.PowerHeader },
            MatchActionType.Goalkeeping => new[] { PlayerTrait.ShotStopper, PlayerTrait.OneOnOnes, PlayerTrait.RushesOutOfGoal, PlayerTrait.Puncher },
            MatchActionType.BallControl => new[] { PlayerTrait.FirstTouch, PlayerTrait.Strong, PlayerTrait.PressResistant, PlayerTrait.TechnicalDribbler },
            MatchActionType.PhysicalDuel => new[] { PlayerTrait.Strong, PlayerTrait.TargetForward, PlayerTrait.AerialThreat, PlayerTrait.BallWinner },
            MatchActionType.Pressing => new[] { PlayerTrait.RelentlessPresser, PlayerTrait.BallWinner, PlayerTrait.Engine, PlayerTrait.BoxToBox },
            MatchActionType.Crossing => new[] { PlayerTrait.CrossingSpecialist, PlayerTrait.Playmaker, PlayerTrait.LongPasser },
            MatchActionType.RecoveryDefending => new[] { PlayerTrait.RecoveryPace, PlayerTrait.Rapid, PlayerTrait.Interceptor, PlayerTrait.BallWinner },
            MatchActionType.CrossClaiming => new[] { PlayerTrait.CrossClaimer, PlayerTrait.Puncher, PlayerTrait.ShotStopper },
            _ => []
        };

        return relevant.Where(player.Traits.Contains);
    }

    private static double GetTraitBonus(PlayerTrait trait, MatchActionType actionType)
    {
        if (trait == PlayerTrait.ClinicalFinisher && actionType == MatchActionType.Finishing ||
            trait == PlayerTrait.Playmaker && actionType == MatchActionType.ChanceCreation ||
            trait == PlayerTrait.Interceptor && actionType == MatchActionType.Interception ||
            trait == PlayerTrait.FirstTouch && actionType is MatchActionType.FirstTouch or MatchActionType.BallControl ||
            trait == PlayerTrait.Strong && actionType is MatchActionType.PhysicalDuel or MatchActionType.AerialDuel ||
            trait == PlayerTrait.ShotStopper && actionType == MatchActionType.Goalkeeping ||
            trait == PlayerTrait.CrossClaimer && actionType == MatchActionType.CrossClaiming)
        {
            return 5.0;
        }

        return 3.5;
    }
}
