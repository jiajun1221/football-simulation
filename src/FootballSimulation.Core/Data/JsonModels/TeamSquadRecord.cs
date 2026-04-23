namespace FootballSimulation.Data.JsonModels;

public class TeamSquadRecord
{
    public string TeamId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Formation { get; set; } = string.Empty;
    public List<SquadPlayerRecord> StartingXI { get; set; } = [];
    public List<SquadPlayerRecord> Substitutes { get; set; } = [];
}
