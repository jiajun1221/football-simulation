namespace FootballSimulation.Data.JsonModels;

public class PremierLeaguePlayersFile
{
    public string Season { get; set; } = string.Empty;
    public List<PlayerDataRecord> Players { get; set; } = [];
}
