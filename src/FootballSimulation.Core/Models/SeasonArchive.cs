namespace FootballSimulation.Models;

public class SeasonArchive
{
    public string Season { get; set; } = string.Empty;
    public string LeagueId { get; set; } = string.Empty;
    public string LeagueName { get; set; } = string.Empty;
    public DateTime CompletedAt { get; set; } = DateTime.Now;
    public string SelectedClubName { get; set; } = string.Empty;
    public int SelectedClubPosition { get; set; }
    public string SelectedClubOutcome { get; set; } = string.Empty;
    public List<ArchivedLeagueTableRow> FinalTable { get; set; } = [];
    public List<ArchivedPlayerStatRow> PlayerStats { get; set; } = [];
    public SeasonAwards Awards { get; set; } = new();
    public List<SeasonHighlight> Highlights { get; set; } = [];
    public BudgetRolloverSummary BudgetSummary { get; set; } = new();
}

public class ArchivedLeagueTableRow
{
    public int Position { get; set; }
    public string TeamName { get; set; } = string.Empty;
    public int Played { get; set; }
    public int Wins { get; set; }
    public int Draws { get; set; }
    public int Losses { get; set; }
    public int GoalsFor { get; set; }
    public int GoalsAgainst { get; set; }
    public int GoalDifference { get; set; }
    public int Points { get; set; }
}

public class ArchivedPlayerStatRow
{
    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public Position Position { get; set; }
    public string ExactPosition { get; set; } = string.Empty;
    public int Appearances { get; set; }
    public int Goals { get; set; }
    public int Assists { get; set; }
    public int Saves { get; set; }
    public int CleanSheets { get; set; }
    public int YellowCards { get; set; }
    public int RedCards { get; set; }
    public double AverageRating { get; set; }
    public int MinutesPlayed { get; set; }
}

public class SeasonAwards
{
    public SeasonAwardWinner PlayerOfTheSeason { get; set; } = new();
    public SeasonAwardWinner YoungPlayerOfTheSeason { get; set; } = new();
    public List<BestXiPlayer> BestXi { get; set; } = [];
}

public class SeasonAwardWinner
{
    public string AwardName { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public double Score { get; set; }
    public string Summary { get; set; } = string.Empty;
}

public class BestXiPlayer
{
    public string Slot { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public int OverallRating { get; set; }
    public double AverageRating { get; set; }
    public int Goals { get; set; }
    public int Assists { get; set; }
    public int Saves { get; set; }
}

public class SeasonHighlight
{
    public string Icon { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string PrimaryText { get; set; } = string.Empty;
    public string SecondaryText { get; set; } = string.Empty;
}

public class BudgetRolloverSummary
{
    public string ClubName { get; set; } = string.Empty;
    public decimal RemainingCarryover { get; set; }
    public decimal BaseBudget { get; set; }
    public decimal PerformanceBonus { get; set; }
    public decimal QualificationBonus { get; set; }
    public decimal NewBudget { get; set; }
    public string Qualification { get; set; } = string.Empty;
}
