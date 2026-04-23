namespace FootballSimulation.Models;

public class MatchEvent
{
    public int Minute { get; set; }
    public EventType EventType { get; set; }
    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }
    public string? PrimaryPlayerName { get; set; }
    public string? SecondaryPlayerName { get; set; }
    public string Description { get; set; } = string.Empty;
}
