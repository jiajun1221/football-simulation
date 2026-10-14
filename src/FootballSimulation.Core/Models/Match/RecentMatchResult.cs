namespace FootballSimulation.Models;

public class RecentMatchResult
{
    public int RoundNumber { get; set; }
    public string OpponentName { get; set; } = string.Empty;
    public string ScoreText { get; set; } = string.Empty;
    public string ResultType { get; set; } = string.Empty;
}
