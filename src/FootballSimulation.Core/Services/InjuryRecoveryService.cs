using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class InjuryRecoveryService
{
    public void AdvanceRecoveryAfterCompletedRound(IEnumerable<Team> teams)
    {
        foreach (var player in teams.SelectMany(team => team.Players.Concat(team.Substitutes)))
        {
            AdvanceSuspension(player);

            if (!player.IsInjured)
            {
                continue;
            }

            if (player.NewlyInjuredThisMatch)
            {
                player.NewlyInjuredThisMatch = false;
                continue;
            }

            if (player.IsSeasonEndingInjury)
            {
                continue;
            }

            player.InjuryRecoveryMatches = Math.Max(0, player.InjuryRecoveryMatches - 1);
            if (player.InjuryRecoveryMatches == 0)
            {
                ClearInjury(player);
            }
        }
    }

    private static void AdvanceSuspension(Player player)
    {
        if (player.SuspendedMatches <= 0)
        {
            player.NewlySuspendedThisMatch = false;
            return;
        }

        if (player.NewlySuspendedThisMatch)
        {
            player.NewlySuspendedThisMatch = false;
            return;
        }

        player.SuspendedMatches--;
    }

    private static void ClearInjury(Player player)
    {
        player.IsInjured = false;
        player.InjuryType = string.Empty;
        player.InjurySeverity = null;
        player.InjuryRecoveryMatches = 0;
        player.IsSeasonEndingInjury = false;
        player.NewlyInjuredThisMatch = false;
    }
}
