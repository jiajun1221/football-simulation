namespace FootballSimulation.Models;

public class PlayerSeasonStats
{
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string TeamId { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public Position Position { get; set; }
    public string ExactPosition { get; set; } = string.Empty;
    public int Appearances { get; set; }
    public int Starts { get; set; }
    public int Goals { get; set; }
    public int Assists { get; set; }
    public int Saves { get; set; }
    public int GoalsConceded { get; set; }
    public int CleanSheets { get; set; }
    public int YellowCards { get; set; }
    public int RedCards { get; set; }
    public double AverageRating { get; set; }
    public int MinutesPlayed { get; set; }
}
