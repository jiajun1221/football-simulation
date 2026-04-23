namespace FootballSimulation.Data.JsonModels;

public class PremierLeagueSquadsFile
{
    public string Season { get; set; } = string.Empty;
    public string SourceLastChecked { get; set; } = string.Empty;
    public List<TeamSquadRecord> Teams { get; set; } = [];
}
