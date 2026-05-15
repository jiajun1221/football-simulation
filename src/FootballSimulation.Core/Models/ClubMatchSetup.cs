namespace FootballSimulation.Models;

public class ClubMatchSetup
{
    public string ClubName { get; set; } = string.Empty;
    public string Formation { get; set; } = string.Empty;
    public List<LineupSlotAssignment> StartingXI { get; set; } = [];
    public List<string> Bench { get; set; } = [];
    public TeamTactics Tactics { get; set; } = new();
}

public class LineupSlotAssignment
{
    public string Slot { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
}
