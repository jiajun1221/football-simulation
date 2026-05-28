using FootballSimulation.Models;

namespace FootballSimulation.Services;

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
                string.Empty);
        }

        return new TransferWindowInfo(false, "Closed", 0, "Transfer window is closed");
    }

    public bool IsWindowOpen(League league, int currentRound)
    {
        return GetWindowInfo(league, currentRound).IsOpen;
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
