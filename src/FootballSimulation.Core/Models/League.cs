namespace FootballSimulation.Models;

public class League
{
    public string Name { get; set; } = string.Empty;
    public List<Team> Teams { get; set; } = [];
    public List<Fixture> Fixtures { get; set; } = [];
    public List<LeagueTableEntry> Table { get; set; } = [];
}
