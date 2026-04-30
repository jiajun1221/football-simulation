using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class FatigueService
{
    private const double BaseStaminaLossPerMinute = 0.45;
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
        foreach (var player in team.Players.Where(player => !player.IsSentOff))
        {
            var staminaLoss = BaseStaminaLossPerMinute
                * GetTempoMultiplier(team.Tactics.Tempo)
                * GetPressingMultiplier(team.Tactics.PressingIntensity)
                * GetPositionMultiplier(player.Position)
                * GetStaminaResistanceMultiplier(player.Stamina)
                * GetPositionSuitabilityFatigueMultiplier(player)
                * GetActivityMultiplier(match, team, player);

            player.Stamina = Math.Clamp(player.Stamina - staminaLoss, 0, 100);
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
            < 40 => 0.80,
            > 70 => 1.30,
            _ => 1.00
        };
    }

    private static double GetPressingMultiplier(int pressing)
    {
        return pressing switch
        {
            < 40 => 0.70,
            > 70 => 1.50,
            _ => 1.00
        };
    }

    private static double GetPositionMultiplier(Position position)
    {
        return position switch
        {
            Position.Goalkeeper => 1.05,
            Position.Defender => 1.10,
            Position.Midfielder => 1.15,
            Position.Forward => 1.20,
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
            >= 0.90 => 1.08,
            _ => 1.18
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

        var defensiveActions = performance.Tackles + performance.Interceptions + performance.Blocks + performance.Clearances;
        var activityScore =
            performance.Shots +
            performance.KeyPasses +
            performance.Fouls +
            performance.Offsides +
            performance.Saves +
            defensiveActions;

        return Math.Clamp(1.0 + activityScore * 0.015, 1.0, 1.18);
    }

}
