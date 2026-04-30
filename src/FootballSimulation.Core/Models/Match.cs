namespace FootballSimulation.Models;

public class Match
{
    public Team HomeTeam { get; set; } = new();
    public Team AwayTeam { get; set; } = new();
    public MatchTeamStats HomeStats { get; set; } = new();
    public MatchTeamStats AwayStats { get; set; } = new();
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
    public int CurrentMinute { get; set; }
    public int HomePossessionMoments { get; set; }
    public int AwayPossessionMoments { get; set; }
    public MatchPhase CurrentPhase { get; set; }
    public List<MatchEvent> Events { get; set; } = [];
    public List<PlayerMatchPerformance> PlayerPerformances { get; set; } = [];
    public List<MatchSubstitution> Substitutions { get; set; } = [];
    public Dictionary<string, int> SuperSubBoosts { get; set; } = [];
}
