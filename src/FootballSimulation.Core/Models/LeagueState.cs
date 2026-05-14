namespace FootballSimulation.Models;

public class LeagueState
{
    public string Name { get; set; } = string.Empty;
    public List<LeagueTableEntry> Table { get; set; } = [];
}
