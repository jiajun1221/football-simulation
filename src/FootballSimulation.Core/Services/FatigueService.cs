using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class FatigueService
{
    private const double BaseFatiguePerMinute = 0.20;

    public void ApplyMinuteFatigue(Team team)
    {
        foreach (var player in team.Players.Where(player => !player.IsSentOff))
        {
            var fatigueGain = BaseFatiguePerMinute
                * GetTempoMultiplier(team.Tactics.Tempo)
                * GetPressingMultiplier(team.Tactics.PressingIntensity)
                * GetPositionMultiplier(player.Position)
                * GetStaminaResistanceMultiplier(player.Stamina);

            player.Fatigue = Math.Clamp((int)Math.Round(player.Fatigue + fatigueGain), 0, 100);
            player.CurrentStamina = CalculateCurrentStamina(player);
        }
    }

    public int GetFatiguePercentage(Player player)
    {
        if (player.Fatigue > 0)
        {
            return Math.Clamp(player.Fatigue, 0, 100);
        }

        if (player.Stamina <= 0)
        {
            return 100;
        }

        var staminaRatio = Math.Clamp(player.CurrentStamina / player.Stamina, 0.0, 1.0);
        return (int)Math.Round((1.0 - staminaRatio) * 100);
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

    private static double GetStaminaResistanceMultiplier(int stamina)
    {
        return Math.Clamp(1.30 - stamina / 100.0, 0.45, 1.20);
    }

    private static double CalculateCurrentStamina(Player player)
    {
        if (player.Stamina <= 0)
        {
            return 0;
        }

        return Math.Clamp(player.Stamina * ((100 - player.Fatigue) / 100.0), 0, player.Stamina);
    }
}
