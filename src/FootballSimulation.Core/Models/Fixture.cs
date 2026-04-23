namespace FootballSimulation.Models;

public class Fixture
{
    public int RoundNumber { get; set; }
    public Team HomeTeam { get; set; } = new();
    public Team AwayTeam { get; set; } = new();
    public bool IsPlayed { get; set; }
    public Match? Result { get; set; }
}
