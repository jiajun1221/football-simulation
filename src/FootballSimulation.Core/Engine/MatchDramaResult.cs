using FootballSimulation.Models;

namespace FootballSimulation.Engine;

public class MatchDramaResult
{
    public required EventType EventType { get; init; }
    public required Team Team { get; init; }
    public Player? Player { get; init; }
    public bool ScoresGoal { get; init; }
    public bool PenaltyConverted { get; init; }
    public double HomeAttackModifier { get; init; } = 1.0;
    public double AwayAttackModifier { get; init; } = 1.0;
    public double HomeDefenseModifier { get; init; } = 1.0;
    public double AwayDefenseModifier { get; init; } = 1.0;
}
