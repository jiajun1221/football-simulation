namespace FootballSimulation.Models;

public class TacticalInsight
{
    public List<string> OpponentThreats { get; set; } = [];
    public List<string> LikelyTactics { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<string> Recommendations { get; set; } = [];
}
