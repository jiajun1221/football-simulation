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
            CompetitionType.CopaDelRey => "CDR",
            CompetitionType.DfbPokal => "DFB",
            CompetitionType.CoppaItalia => "CI",
            CompetitionType.CoupeDeFrance => "CDF",
            CompetitionType.EuropaLeague => "UEL",
            CompetitionType.ConferenceLeague => "UECL",
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
            CompetitionType.CopaDelRey => "#B91C1C",
            CompetitionType.DfbPokal => "#111827",
            CompetitionType.CoppaItalia => "#15803D",
            CompetitionType.CoupeDeFrance => "#2563EB",
            CompetitionType.EuropaLeague => "#F97316",
            CompetitionType.ConferenceLeague => "#16A34A",
            _ => "#64748B"
        };
    }
}
