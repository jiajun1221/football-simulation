namespace FootballSimulation.Models;

public class MatchSubstitution
{
    public int Minute { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public string PlayerOffName { get; set; } = string.Empty;
    public string PlayerOnName { get; set; } = string.Empty;
}
