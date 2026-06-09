using System.Diagnostics;
using FootballSimulation.Models;
using FootballSimulation.Wpf.Models;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Services;

public static class TrophyCelebrationService
{
    private const string DefaultBackgroundImagePath = "pack://application:,,,/Assets/Backgrounds/main-menu-stadium.png";
    public const string DefaultTrophyImagePath = "pack://application:,,,/Assets/Trophies/default.png";

    public static void EnqueuePostMatchCelebrations(GameFlowState state, TrophyCelebrationNextRoute nextRoute)
    {
        if (state.League is null || state.SelectedTeam is null)
        {
            return;
        }

        if (state.CurrentFixture is { } fixture)
        {
            EnqueueCupFinalCelebration(state, fixture, nextRoute);
        }

        EnqueueLeagueTitleCelebrationIfClinched(state, nextRoute);
    }

    public static void EnqueueSeasonResultCelebration(GameFlowState state)
    {
        if (state.League is null ||
            state.SelectedTeam is null ||
            state.League.HasShownLeagueTrophyCelebration ||
            !IsSelectedClubChampion(state.League, state.SelectedTeam))
        {
            return;
        }

        EnqueueLeagueTitleCelebration(state, TrophyCelebrationNextRoute.SeasonOverview);
    }

    public static bool IsTrophyImageAvailable(string trophyImagePath)
    {
        if (string.IsNullOrWhiteSpace(trophyImagePath))
        {
            return false;
        }

        try
        {
            return System.Windows.Application.GetResourceStream(new Uri(trophyImagePath, UriKind.Absolute)) is not null;
        }
        catch
        {
            return false;
        }
    }

    public static string ResolveTrophyImagePath(string trophyImagePath, string competitionName)
    {
        if (IsTrophyImageAvailable(trophyImagePath))
        {
            return trophyImagePath;
        }

        Debug.WriteLine($"Missing trophy image for {competitionName}: {trophyImagePath}. Using default trophy image.");
        return DefaultTrophyImagePath;
    }

    private static void EnqueueCupFinalCelebration(GameFlowState state, Fixture fixture, TrophyCelebrationNextRoute nextRoute)
    {
        if (state.League is null ||
            state.SelectedTeam is null ||
            !fixture.IsKnockout ||
            !IsFinal(fixture) ||
            !fixture.WinningTeamName.Equals(state.SelectedTeam.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var definition = CreateCupDefinition(fixture.Competition, state.League.LeagueId);
        var key = CreateCelebrationKey(state.League.Season, definition.CompetitionId);
        EnqueueIfNew(
            state,
            key,
            definition,
            CreateCupMessage(definition.CompetitionType, state.SelectedTeam.Name, definition.CompetitionName),
            nextRoute);
    }

    private static void EnqueueLeagueTitleCelebrationIfClinched(GameFlowState state, TrophyCelebrationNextRoute nextRoute)
    {
        if (state.League is null ||
            state.SelectedTeam is null ||
            state.League.HasShownLeagueTrophyCelebration ||
            !IsLeagueTitleClinched(state.League, state.SelectedTeam))
        {
            return;
        }

        EnqueueLeagueTitleCelebration(state, nextRoute);
    }

    private static void EnqueueLeagueTitleCelebration(GameFlowState state, TrophyCelebrationNextRoute nextRoute)
    {
        if (state.League is null || state.SelectedTeam is null)
        {
            return;
        }

        var definition = CreateLeagueDefinition(state.League);
        var key = CreateCelebrationKey(state.League.Season, definition.CompetitionId);
        EnqueueIfNew(
            state,
            key,
            definition,
            $"{state.SelectedTeam.Name} have been crowned {definition.CompetitionName} champions!",
            nextRoute);
    }

    private static bool EnqueueIfNew(
        GameFlowState state,
        string key,
        TrophyDefinition definition,
        string message,
        TrophyCelebrationNextRoute nextRoute)
    {
        if (state.League is null ||
            state.SelectedTeam is null)
        {
            return false;
        }

        state.League.ShownTrophyCelebrationKeys ??= [];
        if (state.League.ShownTrophyCelebrationKeys.Contains(key, StringComparer.OrdinalIgnoreCase) ||
            state.TrophyCelebrationQueue.Any(item => item.CelebrationKey.Equals(key, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        state.League.ShownTrophyCelebrationKeys.Add(key);
        state.TrophyCelebrationQueue.Enqueue(new TrophyCelebrationEvent(
            key,
            definition.CompetitionId,
            definition.CompetitionName,
            definition.CompetitionType,
            definition.LeagueId,
            state.SelectedTeam.Name,
            state.League.Season,
            definition.TrophyImagePath,
            definition.BackgroundImagePath,
            definition.ThemeColor,
            definition.AccentColor,
            definition.CelebrationTitle,
            definition.CelebrationSubtitle,
            message,
            nextRoute));
        return true;
    }

    private static bool IsLeagueTitleClinched(League league, Team selectedTeam)
    {
        var selectedRow = league.Table.FirstOrDefault(row =>
            row.TeamName.Equals(selectedTeam.Name, StringComparison.OrdinalIgnoreCase));
        if (selectedRow is null)
        {
            return false;
        }

        return league.Table
            .Where(row => !row.TeamName.Equals(selectedTeam.Name, StringComparison.OrdinalIgnoreCase))
            .All(row => row.Points + CountRemainingLeagueMatches(league, row.TeamName) * 3 < selectedRow.Points);
    }

    private static bool IsSelectedClubChampion(League league, Team selectedTeam)
    {
        var champion = league.Table
            .OrderByDescending(entry => entry.Points)
            .ThenByDescending(entry => entry.GoalDifference)
            .ThenByDescending(entry => entry.GoalsFor)
            .FirstOrDefault();
        return champion is not null &&
            champion.TeamName.Equals(selectedTeam.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static int CountRemainingLeagueMatches(League league, string teamName)
    {
        return league.Fixtures.Count(fixture =>
            !fixture.IsPlayed &&
            fixture.AffectsLeagueTable &&
            (fixture.HomeTeam.Name.Equals(teamName, StringComparison.OrdinalIgnoreCase) ||
                fixture.AwayTeam.Name.Equals(teamName, StringComparison.OrdinalIgnoreCase)));
    }

    private static TrophyDefinition CreateLeagueDefinition(League league)
    {
        var leagueId = string.IsNullOrWhiteSpace(league.LeagueId) ? "premier-league" : league.LeagueId;
        var (name, trophy, theme, accent, title) = leagueId.ToLowerInvariant() switch
        {
            "la-liga" or "laliga" => ("La Liga", "pack://application:,,,/Assets/Trophies/laliga.png", "#7F1D1D", "#FACC15", "La Liga Champions"),
            "bundesliga" => ("Bundesliga", "pack://application:,,,/Assets/Trophies/bundesliga.png", "#111827", "#EF4444", "Bundesliga Champions"),
            "serie-a" => ("Serie A", "pack://application:,,,/Assets/Trophies/serie-a.png", "#064E3B", "#22C55E", "Serie A Champions"),
            "ligue-1" => ("Ligue 1", "pack://application:,,,/Assets/Trophies/ligue-1.png", "#0F172A", "#38BDF8", "Ligue 1 Champions"),
            _ => ("Premier League", DefaultTrophyImagePath, "#07110B", "#FACC15", "Premier League Champions")
        };

        return new TrophyDefinition(
            $"league-{leagueId}",
            string.IsNullOrWhiteSpace(league.Name) ? name : league.Name,
            CompetitionType.PremierLeague,
            leagueId,
            trophy,
            DefaultBackgroundImagePath,
            theme,
            accent,
            title,
            "A title-winning campaign is confirmed.");
    }

    private static TrophyDefinition CreateCupDefinition(CompetitionType competition, string leagueId)
    {
        var normalizedLeagueId = string.IsNullOrWhiteSpace(leagueId) ? "premier-league" : leagueId;
        var displayCompetition = competition == CompetitionType.FACup
            ? GetPrimaryDomesticCupForLeague(normalizedLeagueId)
            : competition;
        var name = CompetitionNames.GetDisplayName(displayCompetition);
        var (trophy, theme, accent, title) = displayCompetition switch
        {
            CompetitionType.FACup => ("pack://application:,,,/Assets/Trophies/fa-cup.png", "#7F1D1D", "#FDE68A", "FA Cup Winners"),
            CompetitionType.LeagueCup => ("pack://application:,,,/Assets/Trophies/league-cup.png", "#064E3B", "#BBF7D0", "League Cup Winners"),
            CompetitionType.CopaDelRey => ("pack://application:,,,/Assets/Trophies/copa-del-rey.png", "#7F1D1D", "#FACC15", "Copa del Rey Winners"),
            CompetitionType.DfbPokal => ("pack://application:,,,/Assets/Trophies/dfb-pokal.png", "#111827", "#EF4444", "DFB-Pokal Winners"),
            CompetitionType.CoppaItalia => ("pack://application:,,,/Assets/Trophies/coppa-italia.png", "#064E3B", "#22C55E", "Coppa Italia Winners"),
            CompetitionType.CoupeDeFrance => ("pack://application:,,,/Assets/Trophies/coupe-de-france.png", "#1E3A8A", "#93C5FD", "Coupe de France Winners"),
            CompetitionType.ChampionsLeague => ("pack://application:,,,/Assets/Trophies/champions-league.png", "#172554", "#BFDBFE", "Champions League Winners"),
            CompetitionType.EuropaLeague => ("pack://application:,,,/Assets/Trophies/europa-league.png", "#7C2D12", "#FDBA74", "Europa League Winners"),
            CompetitionType.ConferenceLeague => ("pack://application:,,,/Assets/Trophies/conference-league.png", "#064E3B", "#86EFAC", "Conference League Winners"),
            _ => (DefaultTrophyImagePath, "#07110B", "#FACC15", $"{name} Winners")
        };

        return new TrophyDefinition(
            $"cup-{normalizedLeagueId}-{displayCompetition}",
            name,
            displayCompetition,
            normalizedLeagueId,
            trophy,
            DefaultBackgroundImagePath,
            theme,
            accent,
            title,
            "A final victory deserves a trophy celebration.");
    }

    private static CompetitionType GetPrimaryDomesticCupForLeague(string leagueId)
    {
        return leagueId.ToLowerInvariant() switch
        {
            "la-liga" or "laliga" => CompetitionType.CopaDelRey,
            "bundesliga" => CompetitionType.DfbPokal,
            "serie-a" => CompetitionType.CoppaItalia,
            "ligue-1" => CompetitionType.CoupeDeFrance,
            _ => CompetitionType.FACup
        };
    }

    private static string CreateCupMessage(CompetitionType competition, string clubName, string competitionName)
    {
        return competition switch
        {
            CompetitionType.ChampionsLeague => $"{clubName} are champions of Europe!",
            CompetitionType.EuropaLeague => $"{clubName} lifted the Europa League trophy!",
            CompetitionType.ConferenceLeague => $"{clubName} lifted the Conference League trophy!",
            _ => $"{clubName} lifted the {competitionName} after winning the final!"
        };
    }

    private static bool IsFinal(Fixture fixture)
    {
        return fixture.Importance == FixtureImportance.Final ||
            fixture.RoundName.Contains("Final", StringComparison.OrdinalIgnoreCase) ||
            fixture.KnockoutRoundKey.Contains("Final", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateCelebrationKey(string season, string competitionId)
    {
        return $"{season}:{competitionId}".ToLowerInvariant();
    }
}
