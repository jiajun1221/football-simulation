namespace FootballSimulation.Models;

public class PlayerMatchPerformance
{
    public string PlayerName { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public Position Position { get; set; }
    public double Rating { get; set; } = 6.0;
    public int Goals { get; set; }
    public int Assists { get; set; }
    public int Shots { get; set; }
    public int ShotsOnTarget { get; set; }
    public int Saves { get; set; }
    public int KeyPasses { get; set; }
    public int Tackles { get; set; }
    public int Interceptions { get; set; }
    public int Blocks { get; set; }
    public int Clearances { get; set; }
    public int Fouls { get; set; }
    public int YellowCards { get; set; }
    public int RedCards { get; set; }
    public int Offsides { get; set; }
    public int Injuries { get; set; }
    public int FatigueAtStart { get; set; }
    public int FatigueAtEnd { get; set; }
    public bool WasSubstitute { get; set; }
    public bool WasSubbedOn { get; set; }
    public bool WasSubbedOff { get; set; }
    public int? SubstitutionMinute { get; set; }
}
