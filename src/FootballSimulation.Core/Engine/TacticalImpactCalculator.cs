using FootballSimulation.Models;

namespace FootballSimulation.Engine;

public class TacticalImpactCalculator
{
    public double GetAttackModifier(Team team, Team? opponent = null)
    {
        var tactics = Normalize(team.Tactics);
        var modifier =
            GetMentalityAttackModifier(tactics.Mentality) *
            ScaleAroundBalanced(tactics.PressingIntensity, 0.0035) *
            ScaleBalancedShape(tactics.Width, 0.0018) *
            ScaleAroundBalanced(tactics.Tempo, 0.0070) *
            ScaleAroundBalanced(tactics.DefensiveLine, 0.0022) *
            GetFormationAttackMatchupModifier(team.Formation, opponent?.Formation);

        return Math.Clamp(modifier, 0.42, 1.85);
    }

    public double GetDefenseModifier(Team team, Team? opponent = null)
    {
        var tactics = Normalize(team.Tactics);
        var modifier =
            GetMentalityDefenseModifier(tactics.Mentality) *
            ScaleAroundBalanced(tactics.PressingIntensity, 0.0040) *
            ScaleDefensiveLineShape(tactics.DefensiveLine) *
            ScaleBalancedShape(tactics.Width, 0.0032) *
            ScaleInverseAroundBalanced(tactics.Tempo, 0.0035) *
            GetFormationDefenseMatchupModifier(team.Formation, opponent?.Formation);

        return Math.Clamp(modifier, 0.42, 1.85);
    }

    public double GetAttackFlowModifier(Team team)
    {
        var tactics = Normalize(team.Tactics);
        var modifier =
            GetMentalityAttackModifier(tactics.Mentality) *
            ScaleAroundBalanced(tactics.Tempo, 0.0060) *
            ScaleAroundBalanced(tactics.PressingIntensity, 0.0025);

        return Math.Clamp(modifier, 0.55, 1.70);
    }

    public double GetTurnoverRiskModifier(Team attackingTeam, Team defendingTeam)
    {
        var attackingTactics = Normalize(attackingTeam.Tactics);
        var defendingTactics = Normalize(defendingTeam.Tactics);
        var modifier =
            ScaleAroundBalanced(defendingTactics.PressingIntensity, 0.0100) *
            ScaleAroundBalanced(attackingTactics.Tempo, 0.0055) *
            ScaleInverseAroundBalanced(attackingTactics.Width, 0.0018);

        return Math.Clamp(modifier, 0.55, 2.10);
    }

    public double GetFoulModifier(Team defendingTeam)
    {
        var tactics = Normalize(defendingTeam.Tactics);
        var modifier =
            ScaleAroundBalanced(tactics.PressingIntensity, 0.0075) *
            ScaleAroundBalanced(tactics.DefensiveLine, 0.0030);

        return Math.Clamp(modifier, 0.65, 1.85);
    }

    public double GetDefensiveErrorModifier(Team defendingTeam)
    {
        var tactics = Normalize(defendingTeam.Tactics);
        var modifier =
            ScaleAroundBalanced(tactics.DefensiveLine, 0.0050) *
            ScaleAroundBalanced(tactics.PressingIntensity, 0.0030) *
            ScaleAroundBalanced(tactics.Tempo, 0.0020);

        return Math.Clamp(modifier, 0.70, 1.80);
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
            Mentality.UltraDefensive => 0.62,
            Mentality.Defensive => 0.78,
            Mentality.Attacking => 1.28,
            Mentality.AllOutAttack => 1.55,
            _ => 1.00
        };
    }

    private static double GetMentalityDefenseModifier(Mentality mentality)
    {
        return mentality switch
        {
            Mentality.UltraDefensive => 1.42,
            Mentality.Defensive => 1.22,
            Mentality.Attacking => 0.86,
            Mentality.AllOutAttack => 0.68,
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

    private static double ScaleDefensiveLineShape(int value)
    {
        return value switch
        {
            <= 25 => 1.22,
            <= 45 => 1.10,
            <= 70 => 1.00,
            _ => 0.88
        };
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
