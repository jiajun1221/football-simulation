using FootballSimulation.Models;

namespace FootballSimulation.Wpf.Services;

public static class CompetitionDisplayService
{
    public static string GetName(CompetitionType competition)
    {
        return CompetitionNames.GetDisplayName(competition);
    }

    public static string GetShortName(CompetitionType competition)
    {
        return competition switch
        {
            CompetitionType.PremierLeague => "PL",
            CompetitionType.FACup => "FA",
            CompetitionType.LeagueCup => "LC",
            CompetitionType.ChampionsLeague => "UCL",
            _ => competition.ToString()
        };
    }

    public static string GetColor(CompetitionType competition)
    {
        return competition switch
        {
            CompetitionType.PremierLeague => "#2E1065",
            CompetitionType.FACup => "#DC2626",
            CompetitionType.LeagueCup => "#16A34A",
            CompetitionType.ChampionsLeague => "#1D4ED8",
            _ => "#64748B"
        };
    }
}
