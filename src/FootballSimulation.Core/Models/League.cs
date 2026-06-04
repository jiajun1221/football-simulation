namespace FootballSimulation.Models;

public class League
{
    public string LeagueId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;
    public List<Team> Teams { get; set; } = [];
    public List<Fixture> Fixtures { get; set; } = [];
    public List<LeagueTableEntry> Table { get; set; } = [];
    public List<PlayerSeasonStats> PlayerStats { get; set; } = [];
    public List<PlayerCompetitionStats> PlayerCompetitionStats { get; set; } = [];
    public List<SeasonCompetitionState> CompetitionStates { get; set; } = [];
    public List<SeasonArchive> SeasonHistory { get; set; } = [];
}
