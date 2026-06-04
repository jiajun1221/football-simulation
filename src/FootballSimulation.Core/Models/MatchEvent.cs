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
    public string Description { get; set; } = string.Empty;
}

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
