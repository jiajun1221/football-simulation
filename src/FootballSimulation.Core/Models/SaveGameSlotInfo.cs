namespace FootballSimulation.Models;

public class SaveGameSlotInfo
{
    public int SlotNumber { get; set; }
    public bool IsEmpty { get; set; }
    public bool IsCorrupted { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public int SaveVersion { get; set; }
    public DateTime? SavedAt { get; set; }
    public string LeagueId { get; set; } = string.Empty;
    public string LeagueName { get; set; } = string.Empty;
    public string SelectedClubName { get; set; } = string.Empty;
    public int CurrentRound { get; set; }
    public int? LeaguePosition { get; set; }
    public int? Points { get; set; }
    public string FilePath { get; set; } = string.Empty;
}
