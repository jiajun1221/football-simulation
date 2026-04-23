namespace FootballSimulation.Models;

public class Team
{
    public string Name { get; set; } = string.Empty;
    public string Formation { get; set; } = string.Empty;
    public List<Player> Players { get; set; } = [];
    public List<Player> Substitutes { get; set; } = [];
    public TeamTactics Tactics { get; set; } = new();
}
