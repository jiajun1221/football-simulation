using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class PlayerTraitDisplayService
{
    private static readonly IReadOnlyDictionary<PlayerTrait, PlayerTraitDefinition> Definitions = new Dictionary<PlayerTrait, PlayerTraitDefinition>
    {
        [PlayerTrait.FinesseShot] = new("FIN", "🎯", "Finesse Shot", "More likely to attempt curved shots.", ShotAccuracyBonus: 0.15, CurlChanceBonus: 0.10),
        [PlayerTrait.PowerHeader] = new("HDR", "🧠", "Power Header", "More dangerous from headers.", HeaderGoalBonus: 0.20, CornerThreatBonus: 0.10),
        [PlayerTrait.Flair] = new("FLR", "✨", "Flair", "More creative in tight attacking moments.", DribbleEventBonus: 0.14),
        [PlayerTrait.SpeedDribbler] = new("SPD", "⚡", "Speed Dribbler", "More likely to drive past defenders.", DribbleEventBonus: 0.18),
        [PlayerTrait.Playmaker] = new("PM", "🪄", "Playmaker", "Receives more attacks and creates chances.", KeyPassBonus: 0.12, AssistEventBonus: 0.10),
        [PlayerTrait.LongPasser] = new("LPS", "📡", "Long Passer", "More likely to switch play or play through balls.", LongPassBonus: 0.12),
        [PlayerTrait.LongShotTaker] = new("LST", "🚀", "Long Shot Taker", "More likely to shoot from distance.", LongShotBonus: 0.14),
        [PlayerTrait.EarlyCrosser] = new("CRS", "↗", "Early Crosser", "More likely to cross early.", CrossingBonus: 0.12),
        [PlayerTrait.OutsideFootShot] = new("OFS", "🌀", "Outside Foot Shot", "Can attempt outside-foot finishes.", ShotAccuracyBonus: 0.04),
        [PlayerTrait.Leadership] = new("LED", "👑", "Leadership", "Improves team composure.", MoraleBonus: 0.08),
        [PlayerTrait.TeamPlayer] = new("TM", "🤝", "Team Player", "Improves support play.", KeyPassBonus: 0.05),
        [PlayerTrait.DivesIntoTackles] = new("TKL", "🛡", "Dives Into Tackles", "Tackles more often with higher card risk.", TackleBonus: 0.16, FoulRiskBonus: 0.18),
        [PlayerTrait.Interceptor] = new("INT", "🧲", "Interceptor", "Reads passing lanes and wins interceptions.", InterceptionBonus: 0.15),
        [PlayerTrait.OneOnOnes] = new("1v1", "🧤", "One On Ones", "Stronger in one-on-one saves.", SaveBonus: 0.08),
        [PlayerTrait.RushesOutOfGoal] = new("RUSH", "🏃", "Rushes Out Of Goal", "More likely to sweep behind the defensive line.", SweeperKeeperBonus: 0.08),
        [PlayerTrait.Puncher] = new("PCH", "👊", "Puncher", "More likely to punch crosses clear.", CrossClaimBonus: 0.08),
        [PlayerTrait.LongThrower] = new("THR", "🖐", "Long Thrower", "Can start longer counter attacks.", LongPassBonus: 0.06),
        [PlayerTrait.TriesToBeatOffsideTrap] = new("RUN", "🏃", "Tries To Beat Offside Trap", "Makes more runs behind, with offside risk.", OffsideRunBonus: 0.16),
        [PlayerTrait.InjuryProne] = new("INJ", "🩹", "Injury Prone", "More likely to be injured.", InjuryRiskModifier: 1.45),
        [PlayerTrait.PressResistant] = new("PRS", "🧱", "Press Resistant", "Keeps the ball better under pressure.", PressureResistanceBonus: 0.12),
        [PlayerTrait.ClinicalFinisher] = new("FIN+", "☠", "Clinical Finisher", "Higher xG conversion.", ShotAccuracyBonus: 0.12),
        [PlayerTrait.DeadBallSpecialist] = new("FK", "🎯", "Dead Ball Specialist", "Better free kicks and corner delivery.", SetPieceBonus: 0.14),
        [PlayerTrait.Engine] = new("ENG", "🔋", "Engine", "Slower stamina drain.", StaminaDrainModifier: 0.88),
        [PlayerTrait.BoxToBox] = new("B2B", "🔄", "Box To Box", "More involved in attack and defense.", KeyPassBonus: 0.04, InterceptionBonus: 0.06),
        [PlayerTrait.AerialThreat] = new("AIR", "🦅", "Aerial Threat", "Dominant in aerial duels.", HeaderGoalBonus: 0.12),
        [PlayerTrait.Rapid] = new("RAP", "💨", "Rapid", "Makes more high-speed attacking runs.", DribbleEventBonus: 0.10),
        [PlayerTrait.TechnicalDribbler] = new("TEC", "🕺", "Technical Dribbler", "Better close-control dribbling.", DribbleEventBonus: 0.12),
        [PlayerTrait.PenaltySpecialist] = new("PEN", "🎯", "Penalty Specialist", "More reliable from the spot.", ShotAccuracyBonus: 0.10)
    };

    public static PlayerTraitDefinition GetDefinition(PlayerTrait trait)
    {
        return Definitions.TryGetValue(trait, out var definition)
            ? definition
            : new PlayerTraitDefinition(trait.ToString(), "•", trait.ToString(), "Special trait affects match behavior.");
    }

    public static string CreateCompactTraitText(IEnumerable<PlayerTrait> traits, int maxVisibleTraits = 3)
    {
        var traitList = traits.Distinct().ToList();
        if (traitList.Count == 0)
        {
            return string.Empty;
        }

        var visibleTraits = traitList
            .Take(maxVisibleTraits)
            .Select(trait => GetDefinition(trait).ShortLabel);
        var extraCount = traitList.Count - maxVisibleTraits;

        return extraCount > 0
            ? $"{string.Join("  ", visibleTraits)}  +{extraCount}"
            : string.Join("  ", visibleTraits);
    }

    public static string CreateTooltip(IEnumerable<PlayerTrait> traits)
    {
        var traitList = traits.Distinct().ToList();
        if (traitList.Count == 0)
        {
            return "No special traits";
        }

        return string.Join(Environment.NewLine, traitList.Select(trait => $"{GetLabel(trait)}: {GetEffectDescription(trait)}"));
    }

    public static string GetShortLabel(PlayerTrait trait)
    {
        return GetDefinition(trait).ShortLabel;
    }

    public static string GetIcon(PlayerTrait trait)
    {
        return GetDefinition(trait).Icon;
    }

    public static string GetLabel(PlayerTrait trait)
    {
        return GetDefinition(trait).Label;
    }

    public static string GetEffectDescription(PlayerTrait trait)
    {
        return GetDefinition(trait).Description;
    }
}

public sealed record PlayerTraitDefinition(
    string ShortLabel,
    string Icon,
    string Label,
    string Description,
    double ShotAccuracyBonus = 0.0,
    double CurlChanceBonus = 0.0,
    double HeaderGoalBonus = 0.0,
    double CornerThreatBonus = 0.0,
    double KeyPassBonus = 0.0,
    double AssistEventBonus = 0.0,
    double DribbleEventBonus = 0.0,
    double LongPassBonus = 0.0,
    double LongShotBonus = 0.0,
    double CrossingBonus = 0.0,
    double MoraleBonus = 0.0,
    double TackleBonus = 0.0,
    double FoulRiskBonus = 0.0,
    double InterceptionBonus = 0.0,
    double SaveBonus = 0.0,
    double SweeperKeeperBonus = 0.0,
    double CrossClaimBonus = 0.0,
    double OffsideRunBonus = 0.0,
    double InjuryRiskModifier = 1.0,
    double PressureResistanceBonus = 0.0,
    double SetPieceBonus = 0.0,
    double StaminaDrainModifier = 1.0);
