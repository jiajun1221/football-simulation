namespace FootballSimulation.Engine;

public static class FormationModifiers
{
    public static double GetAttackModifier(string formation)
    {
        return NormalizeFormation(formation) switch
        {
            "4-3-3" => 1.08,
            "4-2-3-1" => 1.03,
            "4-4-2" => 1.00,
            "3-5-2" => 1.04,
            _ => 1.00
        };
    }

    public static double GetDefenseModifier(string formation)
    {
        return NormalizeFormation(formation) switch
        {
            "4-3-3" => 0.96,
            "4-2-3-1" => 1.05,
            "4-4-2" => 1.00,
            "3-5-2" => 0.94,
            _ => 1.00
        };
    }

    private static string NormalizeFormation(string formation)
    {
        return string.IsNullOrWhiteSpace(formation) ? string.Empty : formation.Trim();
    }
}
