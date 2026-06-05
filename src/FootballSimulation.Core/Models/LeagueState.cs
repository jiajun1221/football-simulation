namespace FootballSimulation.Models;

public class LeagueState
{
    public string LeagueId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;
    public List<LeagueTableEntry> Table { get; set; } = [];
    public bool IsCompleted { get; set; }
}
