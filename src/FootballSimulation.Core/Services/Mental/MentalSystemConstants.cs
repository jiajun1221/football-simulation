namespace FootballSimulation.Services;

public static class MentalSystemConstants
{
    // Mental effects are relative multipliers and intentionally smaller than core attribute effects.
    public const double MaximumGeneralActionModifier = 0.03;
    public const double MaximumFoulAndCardModifier = 0.05;
    public const double MaximumChemistryModifier = 0.05;
    public const int MaximumRecentChanges = 12;
    public const int MaximumRecentDressingRoomEvents = 20;
}
