using FootballSimulation.Models;

namespace FootballSimulation.Engine;

public class TacticalImpactCalculator
{
    public double GetAttackModifier(Team team, Team? opponent = null)
    {
        var tactics = Normalize(team.Tactics);
        var modifier =
            GetMentalityAttackModifier(tactics.Mentality) *
            ScaleAroundBalanced(tactics.PressingIntensity, 0.0020) *
            ScaleAroundBalanced(tactics.Width, 0.0030) *
            ScaleAroundBalanced(tactics.Tempo, 0.0040) *
            ScaleAroundBalanced(tactics.DefensiveLine, 0.0010) *
            GetFormationAttackMatchupModifier(team.Formation, opponent?.Formation);

        return Math.Clamp(modifier, 0.55, 1.55);
    }

    public double GetDefenseModifier(Team team, Team? opponent = null)
    {
        var tactics = Normalize(team.Tactics);
        var modifier =
            GetMentalityDefenseModifier(tactics.Mentality) *
            ScaleAroundBalanced(tactics.PressingIntensity, 0.0020) *
            ScaleAroundBalanced(tactics.DefensiveLine, 0.0030) *
            ScaleBalancedShape(tactics.Width, 0.0020) *
            ScaleInverseAroundBalanced(tactics.Tempo, 0.0020) *
            GetFormationDefenseMatchupModifier(team.Formation, opponent?.Formation);

        return Math.Clamp(modifier, 0.55, 1.55);
    }

    private static TeamTactics Normalize(TeamTactics? tactics)
    {
        tactics ??= new TeamTactics();

        tactics.PressingIntensity = ClampScale(tactics.PressingIntensity);
        tactics.Width = ClampScale(tactics.Width);
        tactics.Tempo = ClampScale(tactics.Tempo);
        tactics.DefensiveLine = ClampScale(tactics.DefensiveLine);

        return tactics;
    }

    private static int ClampScale(int value)
    {
        return Math.Clamp(value, 1, 100);
    }

    private static double GetMentalityAttackModifier(Mentality mentality)
    {
        return mentality switch
        {
            Mentality.Defensive => 0.82,
            Mentality.Attacking => 1.25,
            _ => 1.00
        };
    }

    private static double GetMentalityDefenseModifier(Mentality mentality)
    {
        return mentality switch
        {
            Mentality.Defensive => 1.25,
            Mentality.Attacking => 0.82,
            _ => 1.00
        };
    }

    private static double ScaleAroundBalanced(int value, double weight)
    {
        return 1.0 + ((value - 50) * weight);
    }

    private static double ScaleInverseAroundBalanced(int value, double weight)
    {
        return 1.0 - ((value - 50) * weight);
    }

    private static double ScaleBalancedShape(int value, double weight)
    {
        return 1.0 - (Math.Abs(value - 50) * weight);
    }

    private static double GetFormationAttackMatchupModifier(string formation, string? opponentFormation)
    {
        return (formation, opponentFormation) switch
        {
            ("4-3-3", "3-5-2") => 1.12,
            ("4-2-3-1", "4-3-3") => 1.10,
            ("3-5-2", "4-4-2") => 1.12,
            ("4-4-2", "4-2-3-1") => 0.92,
            _ => 1.00
        };
    }

    private static double GetFormationDefenseMatchupModifier(string formation, string? opponentFormation)
    {
        return (formation, opponentFormation) switch
        {
            ("4-2-3-1", "4-3-3") => 1.12,
            ("4-4-2", "3-5-2") => 0.92,
            ("3-5-2", "4-3-3") => 0.90,
            ("4-4-2", "4-3-3") => 1.06,
            _ => 1.00
        };
    }
}
