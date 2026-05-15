namespace FootballSimulation.Models;

public class SaveGameData
{
    public int SaveVersion { get; set; }
    public DateTime SavedAt { get; set; }
    public string SelectedClubName { get; set; } = string.Empty;
    public int CurrentRound { get; set; }
    public LeagueState LeagueState { get; set; } = new();
    public List<Team> Teams { get; set; } = [];
    public List<Fixture> Fixtures { get; set; } = [];
    public List<Match> MatchHistory { get; set; } = [];
    public List<ClubMatchSetup> ClubMatchSetups { get; set; } = [];
}
