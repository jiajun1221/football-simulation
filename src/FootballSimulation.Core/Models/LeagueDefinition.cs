namespace FootballSimulation.Models;

public class LeagueDefinition
{
    public string LeagueId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;
    public string SquadFile { get; set; } = string.Empty;
    public string LogoPath { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public bool IsPlaceholder { get; set; }
}
