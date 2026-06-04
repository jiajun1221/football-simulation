namespace FootballSimulation.Models;

public enum CompetitionType
{
    PremierLeague,
    FACup,
    LeagueCup,
    ChampionsLeague
}

public enum FixtureImportance
{
    Normal,
    High,
    Knockout,
    Final
}

public enum CompetitionStageType
{
    League,
    Group,
    Knockout
}

public class SeasonCompetitionState
{
    public CompetitionType Competition { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public List<string> QualifiedTeamNames { get; set; } = [];
    public List<string> EliminatedTeamNames { get; set; } = [];
    public List<string> RoundOrder { get; set; } = [];
    public string CurrentRoundName { get; set; } = string.Empty;
    public string WinnerTeamName { get; set; } = string.Empty;
    public string RunnerUpTeamName { get; set; } = string.Empty;
    public List<CompetitionStandingRow> Standings { get; set; } = [];
    public List<ChampionsLeagueGroup> ChampionsLeagueGroups { get; set; } = [];
    public List<CompetitionProgressRecord> ProgressRecords { get; set; } = [];
}

public class CompetitionStandingRow
{
    public string TeamName { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public int Played { get; set; }
    public int Wins { get; set; }
    public int Draws { get; set; }
    public int Losses { get; set; }
    public int GoalsFor { get; set; }
    public int GoalsAgainst { get; set; }
    public int GoalDifference => GoalsFor - GoalsAgainst;
    public int Points { get; set; }
}

public class ChampionsLeagueGroup
{
    public string Name { get; set; } = string.Empty;
    public List<string> TeamNames { get; set; } = [];
    public List<CompetitionStandingRow> Table { get; set; } = [];
}

public class CompetitionProgressRecord
{
    public CompetitionType Competition { get; set; }
    public string RoundName { get; set; } = string.Empty;
    public List<string> QualifiedTeamNames { get; set; } = [];
    public List<string> EliminatedTeamNames { get; set; } = [];
}

public class PlayerCompetitionStats : PlayerSeasonStats
{
    public CompetitionType Competition { get; set; }
}

public static class CompetitionNames
{
    public static string GetDisplayName(CompetitionType competition)
    {
        return competition switch
        {
            CompetitionType.PremierLeague => "Premier League",
            CompetitionType.FACup => "FA Cup",
            CompetitionType.LeagueCup => "League Cup",
            CompetitionType.ChampionsLeague => "Champions League",
            _ => competition.ToString()
        };
    }
}
