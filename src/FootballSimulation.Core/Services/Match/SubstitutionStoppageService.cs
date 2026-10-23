using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class SubstitutionStoppageService
{
    public static bool CanMakeSubstitution(EventType? eventType)
    {
        return eventType is EventType.Halftime
            or EventType.Injury
            or EventType.Foul
            or EventType.PenaltyDecision
            or EventType.PenaltyTaker
            or EventType.Penalty
            or EventType.Offside
            or EventType.ThrowIn
            or EventType.Miss
            or EventType.CornerKick
            or EventType.SetPieceDanger
            or EventType.Goal
            or EventType.WonderGoal
            or EventType.VarCheck
            or EventType.VarDecision
            or EventType.YellowCard
            or EventType.RedCard
            or EventType.RefereeControversy
            or EventType.Confrontation
            or EventType.TimeWasting;
    }
}
