using FootballSimulation.Models;
using FootballSimulation.Services;

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
            GetFormationAttackModifier(team.Formation) *
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
            GetFormationDefenseModifier(team.Formation) *
            GetFormationDefenseMatchupModifier(team.Formation, opponent?.Formation);

        return Math.Clamp(modifier, 0.42, 1.85);
    }

    public double GetAttackFlowModifier(Team team)
    {
        var tactics = Normalize(team.Tactics);
        var modifier =
            GetMentalityAttackModifier(tactics.Mentality) *
            ScaleAroundBalanced(tactics.Tempo, 0.0060) *
            ScaleAroundBalanced(tactics.PressingIntensity, 0.0025) *
            GetFormationControlModifier(team.Formation);

        return Math.Clamp(modifier, 0.55, 1.70);
    }

    public double GetTurnoverRiskModifier(Team attackingTeam, Team defendingTeam)
    {
        var attackingTactics = Normalize(attackingTeam.Tactics);
        var defendingTactics = Normalize(defendingTeam.Tactics);
        var modifier =
            GetMentalityTurnoverRiskModifier(attackingTactics.Mentality) *
            ScaleAroundBalanced(defendingTactics.PressingIntensity, 0.0100) *
            ScaleAroundBalanced(attackingTactics.Tempo, 0.0055) *
            ScaleInverseAroundBalanced(attackingTactics.Width, 0.0018) *
            GetFormationRiskModifier(attackingTeam.Formation) *
            GetManDisadvantageTurnoverModifier(attackingTeam, defendingTeam);

        return Math.Clamp(modifier, 0.55, 2.60);
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

    private static double GetMentalityTurnoverRiskModifier(Mentality mentality)
    {
        return mentality switch
        {
            Mentality.UltraDefensive => 0.86,
            Mentality.Defensive => 0.92,
            Mentality.Attacking => 1.12,
            Mentality.AllOutAttack => 1.30,
            _ => 1.00
        };
    }

    private static double GetManDisadvantageTurnoverModifier(Team attackingTeam, Team defendingTeam)
    {
        var attackingCount = GetActivePlayerCount(attackingTeam);
        var defendingCount = GetActivePlayerCount(defendingTeam);
        var deficit = Math.Max(0, defendingCount - attackingCount);

        return deficit switch
        {
            0 => 1.0,
            1 => 1.35,
            2 => 1.75,
            _ => 2.05
        };
    }

    private static int GetActivePlayerCount(Team team)
    {
        return team.Players.Count(player => player.IsOnPitch && !player.IsSentOff && !player.IsInjured && !player.IsSuspended);
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
        formation = FormationCatalogService.NormalizeFormationName(formation);
        opponentFormation = string.IsNullOrWhiteSpace(opponentFormation)
            ? opponentFormation
            : FormationCatalogService.NormalizeFormationName(opponentFormation);

        return (formation, opponentFormation) switch
        {
            ("4-3-3 Attack", "3-5-2") => 1.12,
            ("4-2-3-1 Wide", "4-3-3 Attack") => 1.10,
            ("4-2-4", _) => 1.10,
            ("3-4-3", _) => 1.07,
            ("3-5-2", "4-4-2") => 1.12,
            ("4-4-2", "4-2-3-1 Wide") => 0.92,
            _ => 1.00
        };
    }

    private static double GetFormationDefenseMatchupModifier(string formation, string? opponentFormation)
    {
        formation = FormationCatalogService.NormalizeFormationName(formation);
        opponentFormation = string.IsNullOrWhiteSpace(opponentFormation)
            ? opponentFormation
            : FormationCatalogService.NormalizeFormationName(opponentFormation);

        return (formation, opponentFormation) switch
        {
            ("4-2-3-1 Wide", "4-3-3 Attack") => 1.12,
            ("4-5-1", _) => 1.16,
            ("5-4-1", _) => 1.22,
            ("5-3-2", _) => 1.16,
            ("4-4-2", "3-5-2") => 0.92,
            ("3-5-2", "4-3-3 Attack") => 0.90,
            ("4-4-2", "4-3-3 Attack") => 1.06,
            _ => 1.00
        };
    }

    private static double GetFormationAttackModifier(string formation)
    {
        var profile = FormationProfile.Create(formation);
        var modifier = 1.0;
        modifier += Math.Max(0, profile.Attackers - 2) * 0.11;
        modifier -= Math.Max(0, profile.Defenders - 4) * 0.08;
        modifier += profile.WideAttackers * 0.035;
        modifier += profile.CentralCreators * 0.03;
        return Math.Clamp(modifier, 0.72, 1.38);
    }

    private static double GetFormationDefenseModifier(string formation)
    {
        var profile = FormationProfile.Create(formation);
        var modifier = 1.0;
        modifier += Math.Max(0, profile.Defenders - 4) * 0.13;
        modifier += Math.Max(0, profile.Midfielders - 4) * 0.045;
        modifier -= Math.Max(0, profile.Attackers - 2) * 0.10;
        return Math.Clamp(modifier, 0.68, 1.42);
    }

    private static double GetFormationControlModifier(string formation)
    {
        var profile = FormationProfile.Create(formation);
        var modifier = 1.0;
        modifier += Math.Max(0, profile.Midfielders - 3) * 0.07;
        modifier -= Math.Max(0, profile.Attackers - 3) * 0.05;
        modifier += profile.CentralCreators > 0 ? 0.04 : 0.0;
        return Math.Clamp(modifier, 0.78, 1.28);
    }

    private static double GetFormationRiskModifier(string formation)
    {
        var profile = FormationProfile.Create(formation);
        var modifier = 1.0;
        modifier += Math.Max(0, profile.Attackers - 2) * 0.12;
        modifier -= Math.Max(0, profile.Defenders - 4) * 0.10;
        return Math.Clamp(modifier, 0.70, 1.38);
    }

    private sealed record FormationProfile(
        int Attackers,
        int Midfielders,
        int Defenders,
        int WideAttackers,
        int CentralCreators)
    {
        public static FormationProfile Create(string formation)
        {
            var slots = FormationSlotService.GetSlots(formation);
            return new FormationProfile(
                slots.Count(IsAttackingSlot),
                slots.Count(IsMidfieldSlot),
                slots.Count(IsDefensiveSlot),
                slots.Count(slot => slot is "LW" or "RW" or "LM" or "RM" or "LWB" or "RWB"),
                slots.Count(slot => slot is "CAM" or "CF"));
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
    }
}
