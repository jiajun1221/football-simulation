using FootballSimulation.Models;

namespace FootballSimulation.Data.JsonModels;

public class LeagueDataIndex
{
    public string ActiveSeason { get; set; } = string.Empty;
    public List<LeagueDefinition> Leagues { get; set; } = [];
}
