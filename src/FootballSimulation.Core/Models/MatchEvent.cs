namespace FootballSimulation.Models;

public class MatchEvent
{
    public int Minute { get; set; }
    public string DisplayMinuteText { get; set; } = string.Empty;
    public EventType EventType { get; set; }
    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }
    public string? PrimaryPlayerName { get; set; }
    public string? SecondaryPlayerName { get; set; }
    public PlayerTrait? TriggeredTrait { get; set; }
    public WeatherCondition? WeatherCondition { get; set; }
    public ShotClassification ShotClassification { get; set; } = ShotClassification.Standard;
    public FoulLocation FoulLocation { get; set; } = FoulLocation.OpenPlay;
    public bool IsPenaltyFoul { get; set; }
    public string FouledPlayer { get; set; } = string.Empty;
    public string FoulingPlayer { get; set; } = string.Empty;
    public string FoulingTeam { get; set; } = string.Empty;
    public string AttackingTeam { get; set; } = string.Empty;
    public string AttackNarrativeId { get; set; } = string.Empty;
    public string AttackRoute { get; set; } = string.Empty;
    public string AttackOrigin { get; set; } = string.Empty;
    public string AttackAction { get; set; } = string.Empty;
    public int AttackSequenceStep { get; set; }
    public int AttackFeedTarget { get; set; }
    public string Description { get; set; } = string.Empty;
}

public sealed record AttackNarrativeContext(
    string Id,
    string TeamName,
    string Origin,
    string Route,
    string Character,
    string PlaymakerName,
    string TargetName,
    string DefensivePressure,
    bool IsLateUrgency,
    string OriginPlayerName = "");

public enum FoulLocation
{
    OpenPlay,
    FinalThird,
    PenaltyBox
}

public enum ShotClassification
{
    Standard,
    Header,
    Volley,
    LongShot,
    FreeKick,
    Penalty
}
