namespace FootballSimulation.Models;

public class MatchSimulationOptions
{
    public string? HumanControlledTeamName { get; set; }
    public bool EnableAiSubstitutions { get; set; } = true;
    public bool EnableDynamicFatigue { get; set; } = true;
}
