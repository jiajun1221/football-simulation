namespace FootballSimulation.Models;

public class MatchTeamStats
{
    public double PossessionPercentage { get; set; }
    public int TotalShots { get; set; }
    public int ShotsOnTarget { get; set; }
    public int Fouls { get; set; }
    public int YellowCards { get; set; }
    public int RedCards { get; set; }
    public int Offsides { get; set; }
    public int Corners { get; set; }
    public double ExpectedGoals { get; set; }
    public int Passes { get; set; }
    public double PassAccuracyPercentage { get; set; }
}
