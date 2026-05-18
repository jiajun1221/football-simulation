namespace FootballSimulation.Data.JsonModels;

public class LeagueSquadsFile
{
    public string LeagueId { get; set; } = string.Empty;
    public string LeagueName { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;
    public string SourceLastChecked { get; set; } = string.Empty;
    public List<TeamSquadRecord> Teams { get; set; } = [];
}
