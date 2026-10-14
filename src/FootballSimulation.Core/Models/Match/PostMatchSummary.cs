namespace FootballSimulation.Models;

public class PostMatchSummary
{
    public Match Match { get; set; } = new();
    public List<PlayerMatchPerformance> PlayerPerformances { get; set; } = [];
    public PlayerMatchPerformance? ManOfTheMatch { get; set; }
    public string ManOfTheMatchReason { get; set; } = string.Empty;
}
