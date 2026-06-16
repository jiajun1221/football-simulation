using FootballSimulation.Models;

namespace FootballSimulation.Services;

public enum TransferWindowPhase
{
    Closed,
    Summer,
    SummerDeadline,
    January,
    JanuaryDeadline
}

public class TransferWindowService
{
    private const int SummerWindowEndRound = 4;

    public TransferWindowInfo GetWindowInfo(League league, int currentRound)
    {
        var maxRound = Math.Max(1, league.Fixtures.Count == 0 ? 1 : league.Fixtures.Max(fixture => fixture.RoundNumber));
        var normalizedRound = Math.Clamp(currentRound, 1, maxRound);
        var windows = GetWindows(maxRound);
        var activeWindow = windows.FirstOrDefault(window => normalizedRound >= window.Start && normalizedRound <= window.End);

        if (activeWindow != default)
        {
            return new TransferWindowInfo(
                true,
                "Open",
                Math.Max(0, activeWindow.End - normalizedRound + 1),
                string.Empty,
                GetWindowId(league, currentRound));
        }

        return new TransferWindowInfo(false, "Closed", 0, "Transfer window is closed", string.Empty);
    }

    public bool IsWindowOpen(League league, int currentRound)
    {
        return GetWindowInfo(league, currentRound).IsOpen;
    }

    public TransferWindowPhase GetWindowPhase(League league, int currentRound)
    {
        var maxRound = Math.Max(1, league.Fixtures.Count == 0 ? 1 : league.Fixtures.Max(fixture => fixture.RoundNumber));
        var normalizedRound = Math.Clamp(currentRound, 1, maxRound);
        var windows = GetWindows(maxRound);
        var summer = windows[0];
        var january = windows[1];

        if (normalizedRound >= summer.Start && normalizedRound <= summer.End)
        {
            return normalizedRound == summer.End ? TransferWindowPhase.SummerDeadline : TransferWindowPhase.Summer;
        }

        if (normalizedRound >= january.Start && normalizedRound <= january.End)
        {
            return normalizedRound == january.End ? TransferWindowPhase.JanuaryDeadline : TransferWindowPhase.January;
        }

        return TransferWindowPhase.Closed;
    }

    public bool IsDeadlineRound(League league, int currentRound)
    {
        return GetWindowPhase(league, currentRound) is TransferWindowPhase.SummerDeadline or TransferWindowPhase.JanuaryDeadline;
    }

    public string GetWindowId(League league, int currentRound)
    {
        return GetWindowPhase(league, currentRound) switch
        {
            TransferWindowPhase.Summer or TransferWindowPhase.SummerDeadline => CreateWindowId(league, "summer"),
            TransferWindowPhase.January or TransferWindowPhase.JanuaryDeadline => CreateWindowId(league, "january"),
            _ => string.Empty
        };
    }

    private static string CreateWindowId(League league, string windowName)
    {
        var leagueId = string.IsNullOrWhiteSpace(league.LeagueId) ? "league" : league.LeagueId;
        var season = string.IsNullOrWhiteSpace(league.Season) ? "season" : league.Season;
        return $"{leagueId}:{season}:{windowName}";
    }

    private static List<(int Start, int End)> GetWindows(int maxRound)
    {
        var summerEnd = Math.Min(SummerWindowEndRound, maxRound);
        var winterStart = maxRound >= 22 ? 19 : Math.Max(summerEnd + 1, (int)Math.Round(maxRound * 0.55));
        var winterEnd = maxRound >= 22 ? 22 : Math.Min(maxRound, winterStart + 2);

        return
        [
            (1, summerEnd),
            (winterStart, winterEnd)
        ];
    }
}
