using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class LoanEligibilityService
{
    public static bool CanJoinOnLoan(Player player, out string reason)
    {
        if (player.IsOnLoan)
        {
            reason = "This player is already on loan.";
            return false;
        }

        if (player.Role == PlayerRole.KeyPlayer || player.OverallRating >= 82)
        {
            reason = "Key and elite players are not available for loan.";
            return false;
        }

        if (player.Age is > 25)
        {
            reason = "Established players over 25 are not normally available for loan.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}
