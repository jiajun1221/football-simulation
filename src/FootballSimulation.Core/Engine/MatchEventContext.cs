using FootballSimulation.Models;

namespace FootballSimulation.Engine;

public class MatchEventContext
{
    public required Match Match { get; init; }
    public required Team HomeTeam { get; init; }
    public required Team AwayTeam { get; init; }
    public required Random Random { get; init; }
    public int Minute { get; init; }
    public double HomeAttackStrength { get; init; }
    public double AwayAttackStrength { get; init; }
    public double HomeDefenseStrength { get; init; }
    public double AwayDefenseStrength { get; init; }
}
