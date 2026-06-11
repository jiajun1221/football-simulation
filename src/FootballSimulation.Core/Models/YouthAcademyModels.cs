namespace FootballSimulation.Models;

public enum AcademyLevel
{
    Bronze,
    Silver,
    Gold,
    Elite
}

public enum YouthScoutFocus
{
    Balanced,
    Goalkeeper,
    Defender,
    Midfielder,
    Winger,
    Striker,
    Physical,
    Technical,
    Pace,
    Playmaker
}

public enum YouthDevelopmentRate
{
    Slow,
    Normal,
    Fast,
    Explosive
}

public enum YouthPersonality
{
    Reserved,
    Professional,
    Ambitious,
    Determined,
    Flair,
    Leader
}

public enum YouthPotentialTier
{
    CommonProspect,
    GoodProspect,
    ExcitingProspect,
    EliteProspect,
    GenerationalTalent
}

public enum YouthTrainingFocus
{
    Balanced,
    Technical,
    Physical,
    Attacking,
    Defensive,
    Playmaking
}

public enum YouthScoutRating
{
    JuniorScout,
    RegionalScout,
    SeniorScout,
    EliteScout
}

public enum YouthScoutPositionFocus
{
    AnyPosition,
    ST,
    LW,
    RW,
    CF,
    CAM,
    CM,
    CDM,
    LB,
    RB,
    CB,
    GK
}

public class YouthAcademy
{
    public string ClubId { get; set; } = string.Empty;
    public string ClubName { get; set; } = string.Empty;
    public AcademyLevel AcademyLevel { get; set; } = AcademyLevel.Silver;
    public int Reputation { get; set; } = 50;
    public YouthScoutFocus ScoutFocus { get; set; } = YouthScoutFocus.Balanced;
    public YouthTrainingFocus TrainingFocus { get; set; } = YouthTrainingFocus.Balanced;
    public List<YouthPlayer> YouthPlayers { get; set; } = [];
    public List<YouthIntakeRecord> IntakeHistory { get; set; } = [];
    public List<YouthTransferRecord> TransferHistory { get; set; } = [];
    public List<YouthScoutAssignment> ScoutAssignments { get; set; } = [];
    public List<YouthScoutReport> ScoutReports { get; set; } = [];
}

public class YouthPlayer
{
    public string PlayerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Nationality { get; set; } = string.Empty;
    public string NationalityCode { get; set; } = string.Empty;
    public string NationalityName { get; set; } = string.Empty;
    public string FlagImagePath { get; set; } = string.Empty;
    public int Age { get; set; }
    public Position Position { get; set; }
    public string PreferredPosition { get; set; } = string.Empty;
    public List<string> SecondaryPositions { get; set; } = [];
    public int CurrentOVR { get; set; }
    public int PotentialMin { get; set; }
    public int PotentialMax { get; set; }
    public int HiddenTruePotential { get; set; }
    public List<PlayerTrait> Traits { get; set; } = [];
    public YouthPersonality Personality { get; set; } = YouthPersonality.Professional;
    public YouthDevelopmentRate DevelopmentRate { get; set; } = YouthDevelopmentRate.Normal;
    public YouthPotentialTier PotentialTier { get; set; } = YouthPotentialTier.GoodProspect;
    public decimal MarketValue { get; set; }
    public decimal WeeklyWage { get; set; }
    public string ClubId { get; set; } = string.Empty;
    public string ClubName { get; set; } = string.Empty;
    public bool IsPromoted { get; set; }
    public string ScoutReport { get; set; } = string.Empty;
    public double DevelopmentProgress { get; set; }
    public int IntakeSeason { get; set; }
}

public class YouthIntakeRecord
{
    public string IntakeId { get; set; } = Guid.NewGuid().ToString("N");
    public string Season { get; set; } = string.Empty;
    public int CalendarRound { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int PlayerCount { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> PlayerIds { get; set; } = [];
}

public class YouthTransferRecord
{
    public string TransferId { get; set; } = Guid.NewGuid().ToString("N");
    public int RoundNumber { get; set; }
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string FromClubId { get; set; } = string.Empty;
    public string FromClubName { get; set; } = string.Empty;
    public string ToClubId { get; set; } = string.Empty;
    public string ToClubName { get; set; } = string.Empty;
    public decimal Fee { get; set; }
    public string Type { get; set; } = "Youth";
}

public class YouthScoutAssignment
{
    public string ScoutId { get; set; } = string.Empty;
    public string ScoutName { get; set; } = string.Empty;
    public YouthScoutRating Rating { get; set; } = YouthScoutRating.JuniorScout;
    public string AssignedCountry { get; set; } = "England";
    public string CountryCode { get; set; } = "GB-ENG";
    public string FlagImagePath { get; set; } = "/Assets/Flags/england.png";
    public YouthScoutPositionFocus PrimaryFocus { get; set; } = YouthScoutPositionFocus.AnyPosition;
    public YouthScoutPositionFocus SecondaryFocus { get; set; } = YouthScoutPositionFocus.AnyPosition;
    public int ProgressMatches { get; set; }
    public int RequiredMatches { get; set; } = 3;
    public string ActiveReportId { get; set; } = string.Empty;
    public int LastProgressRound { get; set; }
}

public class YouthScoutReport
{
    public string ReportId { get; set; } = Guid.NewGuid().ToString("N");
    public string ScoutId { get; set; } = string.Empty;
    public string ScoutName { get; set; } = string.Empty;
    public YouthScoutRating ScoutRating { get; set; } = YouthScoutRating.JuniorScout;
    public string Country { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string FlagImagePath { get; set; } = string.Empty;
    public YouthScoutPositionFocus PrimaryFocus { get; set; } = YouthScoutPositionFocus.AnyPosition;
    public YouthScoutPositionFocus SecondaryFocus { get; set; } = YouthScoutPositionFocus.AnyPosition;
    public string Season { get; set; } = string.Empty;
    public int CreatedRound { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<YouthScoutProspect> Prospects { get; set; } = [];
}

public class YouthScoutProspect
{
    public string ProspectId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Nationality { get; set; } = string.Empty;
    public string NationalityCode { get; set; } = string.Empty;
    public string NationalityName { get; set; } = string.Empty;
    public string FlagImagePath { get; set; } = string.Empty;
    public int Age { get; set; }
    public Position Position { get; set; }
    public string PreferredPosition { get; set; } = string.Empty;
    public List<string> SecondaryPositions { get; set; } = [];
    public int CurrentOVR { get; set; }
    public int PotentialMin { get; set; }
    public int PotentialMax { get; set; }
    public int HiddenTruePotential { get; set; }
    public List<PlayerTrait> Traits { get; set; } = [];
    public YouthPersonality Personality { get; set; } = YouthPersonality.Professional;
    public YouthDevelopmentRate DevelopmentRate { get; set; } = YouthDevelopmentRate.Normal;
    public YouthPotentialTier PotentialTier { get; set; } = YouthPotentialTier.GoodProspect;
    public decimal SigningCost { get; set; }
    public decimal WeeklyWage { get; set; }
    public string ScoutNotes { get; set; } = string.Empty;
    public bool IsSigned { get; set; }
    public string SignedByClubId { get; set; } = string.Empty;
    public string SignedByClubName { get; set; } = string.Empty;
}

public sealed record YouthScoutCountry(string Name, string Code, string FlagImagePath)
{
    public override string ToString()
    {
        return Name;
    }
}

public record YouthOperationResult(
    bool Success,
    string Message,
    YouthPlayer? YouthPlayer = null,
    Player? PromotedPlayer = null,
    decimal Fee = 0);
