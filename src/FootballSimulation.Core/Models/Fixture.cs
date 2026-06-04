namespace FootballSimulation.Models;

public class Fixture
{
    public string FixtureId { get; set; } = Guid.NewGuid().ToString("N");
    public int RoundNumber { get; set; }
    public int CalendarRound { get; set; }
    public DateTime? ScheduledDate { get; set; }
    public CompetitionType Competition { get; set; } = CompetitionType.PremierLeague;
    public string RoundName { get; set; } = string.Empty;
    public string Venue { get; set; } = string.Empty;
    public FixtureImportance Importance { get; set; } = FixtureImportance.Normal;
    public bool AffectsLeagueTable { get; set; } = true;
    public bool IsKnockout { get; set; }
    public string KnockoutRoundKey { get; set; } = string.Empty;
    public int? ExtraTimeHomeScore { get; set; }
    public int? ExtraTimeAwayScore { get; set; }
    public int? PenaltyHomeScore { get; set; }
    public int? PenaltyAwayScore { get; set; }
    public string WinningTeamName { get; set; } = string.Empty;
    public string LosingTeamName { get; set; } = string.Empty;
    public Team HomeTeam { get; set; } = new();
    public Team AwayTeam { get; set; } = new();
    public bool IsPlayed { get; set; }
    public Match? Result { get; set; }
}
