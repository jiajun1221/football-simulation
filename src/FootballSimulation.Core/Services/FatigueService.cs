using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class FatigueService
{
    private const double BaseStaminaLossPerMinute = 0.32;
    private const double MaximumDynamicStaminaLossPerMinute = 0.82;
    private const int MinimumMatchStartRecoveryPoints = 50;
    private const int MaximumMatchStartRecoveryPoints = 60;

    public void RecoverTeamForNewMatch(Team team, int? recoveryPoints = null)
    {
        foreach (var player in team.Players.Concat(team.Substitutes))
        {
            var recovery = recoveryPoints ?? Random.Shared.Next(
                MinimumMatchStartRecoveryPoints,
                MaximumMatchStartRecoveryPoints + 1);
            player.Stamina = Math.Clamp(player.Stamina + recovery, 0, 100);
        }
    }

    public void ApplyMinuteFatigue(Team team, Match? match = null)
    {
        foreach (var player in team.Players.Where(player => player.IsOnPitch && !player.IsSentOff && !player.IsSuspended && !player.IsInjured))
        {
            var staminaLoss = BaseStaminaLossPerMinute
                * GetTempoMultiplier(team.Tactics.Tempo)
                * GetPressingMultiplier(team.Tactics.PressingIntensity)
                * GetPositionMultiplier(player.Position)
                * GetStaminaResistanceMultiplier(player.Stamina)
                * GetPositionSuitabilityFatigueMultiplier(player)
                * GetFormationStaminaMultiplier(team, player)
                * GetTraitFatigueMultiplier(player)
                * GetActivityMultiplier(match, team, player);

            player.Stamina = Math.Clamp(player.Stamina - Math.Min(staminaLoss, MaximumDynamicStaminaLossPerMinute), 0, 100);
        }
    }

    public int GetFatiguePercentage(Player player)
    {
        return 100 - GetStaminaPercentage(player);
    }

    public int GetStaminaPercentage(Player player)
    {
        return Math.Clamp((int)Math.Round(player.Stamina), 0, 100);
    }

    private static double GetTempoMultiplier(int tempo)
    {
        return tempo switch
        {
            <= 25 => 0.78,
            < 40 => 0.88,
            >= 85 => 1.24,
            > 70 => 1.14,
            _ => 1.00
        };
    }

    private static double GetPressingMultiplier(int pressing)
    {
        return pressing switch
        {
            <= 25 => 0.72,
            < 40 => 0.86,
            >= 85 => 1.36,
            > 70 => 1.20,
            _ => 1.00
        };
    }

    private static double GetPositionMultiplier(Position position)
    {
        return position switch
        {
            Position.Goalkeeper => 0.88,
            Position.Defender => 1.02,
            Position.Midfielder => 1.08,
            Position.Forward => 1.10,
            _ => 1.00
        };
    }

    private static double GetStaminaResistanceMultiplier(double stamina)
    {
        return Math.Clamp(1.30 - stamina / 100.0, 0.45, 1.20);
    }

    private static double GetPositionSuitabilityFatigueMultiplier(Player player)
    {
        return PositionSuitabilityService.GetEffectivenessMultiplier(player) switch
        {
            >= 1.0 => 1.00,
            >= 0.90 => 1.04,
            _ => 1.10
        };
    }

    private static double GetActivityMultiplier(Match? match, Team team, Player player)
    {
        if (match is null)
        {
            return 1.0;
        }

        var performance = match.PlayerPerformances.FirstOrDefault(existing =>
            existing.PlayerName == player.Name &&
            existing.TeamName == team.Name);

        if (performance is null)
        {
            return 1.0;
        }

        var defensiveActions = performance.Tackles +
            performance.Interceptions +
            performance.Blocks +
            performance.Clearances +
            performance.AerialDuelsWon +
            performance.Recoveries +
            performance.GoalLineClearances;
        var activityScore =
            performance.Shots +
            performance.KeyPasses +
            performance.Fouls +
            performance.Offsides +
            performance.Saves +
            defensiveActions;

        return Math.Clamp(1.0 + activityScore * 0.015, 1.0, 1.18);
    }

    private static double GetFormationStaminaMultiplier(Team team, Player player)
    {
        var slots = FormationSlotService.GetSlots(team.Formation);
        var attackerCount = slots.Count(IsAttackingSlot);
        var defenderCount = slots.Count(IsDefensiveSlot);
        var assignedSlot = PositionSuitabilityService.NormalizeExactPosition(player.AssignedPosition);

        if (IsAttackingSlot(assignedSlot))
        {
            return attackerCount >= 4 ? 1.16 : attackerCount >= 3 ? 1.08 : 1.0;
        }

        if (IsMidfieldSlot(assignedSlot))
        {
            return slots.Count(IsMidfieldSlot) >= 5 ? 1.05 : 1.0;
        }

        if (IsDefensiveSlot(assignedSlot))
        {
            return defenderCount >= 5 ? 0.94 : 1.0;
        }

        return 1.0;
    }

    private static bool IsAttackingSlot(string slot)
    {
        return slot is "ST" or "CF" or "LW" or "RW";
    }

    private static bool IsMidfieldSlot(string slot)
    {
        return slot is "CDM" or "CM" or "CAM" or "LM" or "RM";
    }

    private static bool IsDefensiveSlot(string slot)
    {
        return slot is "CB" or "LB" or "RB" or "LWB" or "RWB";
    }

    private static double GetTraitFatigueMultiplier(Player player)
    {
        return player.Traits.Contains(PlayerTrait.Engine) || player.Traits.Contains(PlayerTrait.BoxToBox)
            ? 0.88
            : 1.0;
    }

}
