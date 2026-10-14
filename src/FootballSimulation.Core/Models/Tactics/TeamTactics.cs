namespace FootballSimulation.Models;

public class TeamTactics
{
    public Mentality Mentality { get; set; } = Mentality.Balanced;
    public int PressingIntensity { get; set; } = 50;
    public int Width { get; set; } = 50;
    public int Tempo { get; set; } = 50;
    public int DefensiveLine { get; set; } = 50;
}
