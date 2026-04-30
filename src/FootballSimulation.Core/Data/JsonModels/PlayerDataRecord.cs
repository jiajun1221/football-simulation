namespace FootballSimulation.Data.JsonModels;

public class PlayerDataRecord
{
    public string Id { get; set; } = string.Empty;
    public string TeamId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SquadNumber { get; set; }
    public string Position { get; set; } = string.Empty;
    public string? PreferredPosition { get; set; }
    public List<string> NaturalPositions { get; set; } = [];
    public List<string> SecondaryPositions { get; set; } = [];
    public int OverallRating { get; set; }
    public int? Stamina { get; set; }
    public bool IsStarter { get; set; }
    public string? Form { get; set; }
    public List<string> Traits { get; set; } = [];
    public int? CurrentForm { get; set; }
    public int? Morale { get; set; }
    public int? Fatigue { get; set; }
    public bool? IsInjured { get; set; }
    public bool? IsSuspended { get; set; }
    public int? MatchesPlayedRecently { get; set; }
}
