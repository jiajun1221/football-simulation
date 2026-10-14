namespace FootballSimulation.Data.JsonModels;

public class PremierLeagueTeamsFile
{
    public string LeagueName { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;
    public List<TeamDataRecord> Teams { get; set; } = [];
}
