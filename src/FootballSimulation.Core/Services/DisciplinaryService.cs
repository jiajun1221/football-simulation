using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class DisciplinaryService
{
    public bool ApplyYellowCard(Player player, MatchTeamStats teamStats)
    {
        player.YellowCards++;
        teamStats.YellowCards++;

        if (player.YellowCards < 2 || player.IsSentOff)
        {
            return false;
        }

        player.IsSentOff = true;
        player.IsOnPitch = false;
        teamStats.RedCards++;
        return true;
    }
}
