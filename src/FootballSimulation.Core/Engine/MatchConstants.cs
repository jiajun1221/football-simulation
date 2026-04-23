namespace FootballSimulation.Engine;

public static class MatchConstants
{
    public const int DefaultMatchDurationMinutes = 90;
    public const int HalftimeMinute = 45;
    public const int MaxSubstitutionsPerTeam = 5;
    public const double StaminaLossPerMinute = 0.30;
    public const double MinimumStaminaModifier = 0.75;
    public const double MaximumStaminaModifier = 1.00;
    public const double BaseFoulChancePerAttack = 0.12;
    public const double YellowCardChancePerFoul = 0.35;

    public const double BaseShotChancePerMinute = 0.14;
    public const double MinimumShotChancePerMinute = 0.05;
    public const double MaximumShotChancePerMinute = 0.40;
    public const double ShotChanceDivisor = 220.0;

    public const double GoalProbabilityBase = 0.10;
    public const double MinimumGoalProbability = 0.05;
    public const double MaximumGoalProbability = 0.45;
    public const double GoalProbabilityDivisor = 180.0;
}
