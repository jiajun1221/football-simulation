namespace FootballSimulation.Models;

public class Team
{
    public string Name { get; set; } = string.Empty;
    public string Venue { get; set; } = string.Empty;
    public string StadiumName { get; set; } = string.Empty;
    public string Formation { get; set; } = string.Empty;
    public List<Player> Players { get; set; } = [];
    public List<Player> Substitutes { get; set; } = [];
    public List<Player> Reserves { get; set; } = [];
    public List<Player> LoanedOutPlayers { get; set; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public IEnumerable<Player> AllPlayers => Players.Concat(Substitutes).Concat(Reserves);
    public TeamTactics Tactics { get; set; } = new();
    public List<FormationPreset> FormationPresets { get; set; } = [];
}
