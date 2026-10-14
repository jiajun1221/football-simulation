using FootballSimulation.Models;

namespace FootballSimulation.Engine;

public static class HomeAwayAdvantageService
{
    public static HomeAwayAdvantageModifier GetModifier(Match match, Team team)
    {
        var isHome = IsHomeTeam(match, team);
        var rivalryMultiplier = match.IsRivalryMatch ? 1.15 : 1.0;
        if (isHome)
        {
            return new HomeAwayAdvantageModifier(
                AttackModifier: 1.0 + 0.050 * rivalryMultiplier,
                DefenseModifier: 1.0 + 0.038 * rivalryMultiplier,
                PassingModifier: 1.0 + 0.035 * rivalryMultiplier,
                FinishingModifier: 1.0 + 0.040 * rivalryMultiplier,
                TurnoverRiskModifier: 0.94,
                FoulRiskModifier: 0.96,
                YellowRiskModifier: 0.96,
                DefensiveErrorRiskModifier: 0.94,
                FatigueLossModifier: 0.99);
        }

        var awayShapeMitigation = GetAwayShapeMitigation(team);
        var highPressPenalty = Math.Max(0, team.Tactics.PressingIntensity - 65) / 1000.0;
        return new HomeAwayAdvantageModifier(
            AttackModifier: 1.0 - (0.060 * rivalryMultiplier * awayShapeMitigation),
            DefenseModifier: 1.0 - (0.038 * rivalryMultiplier * awayShapeMitigation),
            PassingModifier: 1.0 - (0.050 * rivalryMultiplier * awayShapeMitigation),
            FinishingModifier: 1.0 - (0.070 * rivalryMultiplier * awayShapeMitigation),
            TurnoverRiskModifier: 1.0 + (0.090 * rivalryMultiplier * awayShapeMitigation),
            FoulRiskModifier: 1.0 + (0.055 * rivalryMultiplier * awayShapeMitigation),
            YellowRiskModifier: 1.0 + (0.045 * rivalryMultiplier * awayShapeMitigation),
            DefensiveErrorRiskModifier: 1.0 + (0.100 * rivalryMultiplier * awayShapeMitigation),
            FatigueLossModifier: 1.0 + (0.030 * rivalryMultiplier * awayShapeMitigation) + highPressPenalty);
    }

    public static double GetAwayShapeMitigation(Team team)
    {
        var lowBlockBonus = team.Tactics.DefensiveLine <= 40 ? 0.12 : 0.0;
        var defensiveMentalityBonus = team.Tactics.Mentality is Mentality.Defensive or Mentality.UltraDefensive ? 0.14 : 0.0;
        var controlledTempoBonus = team.Tactics.Tempo <= 48 ? 0.06 : 0.0;
        var allOutPenalty = team.Tactics.Mentality == Mentality.AllOutAttack ? -0.10 : 0.0;

        return Math.Clamp(1.0 - lowBlockBonus - defensiveMentalityBonus - controlledTempoBonus - allOutPenalty, 0.65, 1.15);
    }

    public static double GetOffsideAdjustment(Match match, Team attackingTeam)
    {
        if (IsHomeTeam(match, attackingTeam))
        {
            return -0.006;
        }

        var rivalryMultiplier = match.IsRivalryMatch ? 1.2 : 1.0;
        return 0.010 * rivalryMultiplier * GetAwayShapeMitigation(attackingTeam);
    }

    public static bool IsHomeTeam(Match match, Team team)
    {
        return string.Equals(match.HomeTeam.Name, team.Name, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record HomeAwayAdvantageModifier(
    double AttackModifier,
    double DefenseModifier,
    double PassingModifier,
    double FinishingModifier,
    double TurnoverRiskModifier,
    double FoulRiskModifier,
    double YellowRiskModifier,
    double DefensiveErrorRiskModifier,
    double FatigueLossModifier);
