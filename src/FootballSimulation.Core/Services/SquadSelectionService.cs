using FootballSimulation.Engine;
using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class SquadSelectionService
{
    public SquadSwapResult SwapStarterWithSubstitute(
        Team team,
        Player starter,
        Player substitute,
        Match? match = null,
        int? substitutionMinute = null)
    {
        var starterIndex = team.Players.IndexOf(starter);
        if (starterIndex < 0)
        {
            return SquadSwapResult.Failed("The selected starter is no longer in the starting XI.");
        }

        if (!team.Substitutes.Contains(substitute))
        {
            return SquadSwapResult.Failed("The selected substitute is no longer on the bench.");
        }

        if (match is not null && substitutionMinute.HasValue &&
            CountTeamSubstitutions(match, team.Name) >= MatchConstants.MaxSubstitutionsPerTeam)
        {
            return SquadSwapResult.Failed($"Maximum substitutions reached ({MatchConstants.MaxSubstitutionsPerTeam}/5).");
        }

        team.Players[starterIndex] = substitute;
        team.Substitutes.Remove(substitute);
        team.Substitutes.Add(starter);

        starter.IsStarter = false;
        substitute.IsStarter = true;

        if (match is not null && substitutionMinute.HasValue)
        {
            RecordMatchSubstitution(match, team, starter, substitute, substitutionMinute.Value);
        }

        return SquadSwapResult.Succeeded();
    }

    public int CountTeamSubstitutions(Match match, string teamName)
    {
        return match.Substitutions.Count(substitution =>
            string.Equals(substitution.TeamName, teamName, StringComparison.OrdinalIgnoreCase));
    }

    private static void RecordMatchSubstitution(
        Match match,
        Team team,
        Player playerOff,
        Player playerOn,
        int minute)
    {
        match.Substitutions.Add(new MatchSubstitution
        {
            Minute = minute,
            TeamName = team.Name,
            PlayerOffName = playerOff.Name,
            PlayerOnName = playerOn.Name
        });

        var offPerformance = GetOrCreatePerformance(match, team, playerOff);
        offPerformance.WasSubbedOff = true;
        offPerformance.SubstitutionMinute = minute;

        var onPerformance = GetOrCreatePerformance(match, team, playerOn);
        onPerformance.WasSubstitute = true;
        onPerformance.WasSubbedOn = true;
        onPerformance.SubstitutionMinute = minute;
    }

    private static PlayerMatchPerformance GetOrCreatePerformance(Match match, Team team, Player player)
    {
        var performance = match.PlayerPerformances.FirstOrDefault(existing =>
            existing.PlayerName == player.Name &&
            existing.TeamName == team.Name);

        if (performance is not null)
        {
            return performance;
        }

        performance = new PlayerMatchPerformance
        {
            PlayerName = player.Name,
            TeamName = team.Name,
            Position = player.Position,
            FatigueAtStart = GetFatiguePercentage(player),
            FatigueAtEnd = GetFatiguePercentage(player)
        };

        match.PlayerPerformances.Add(performance);
        return performance;
    }

    private static int GetFatiguePercentage(Player player)
    {
        if (player.Fatigue > 0)
        {
            return Math.Clamp(player.Fatigue, 0, 100);
        }

        if (player.Stamina <= 0)
        {
            return 100;
        }

        var staminaRatio = Math.Clamp(player.CurrentStamina / player.Stamina, 0.0, 1.0);
        return (int)Math.Round((1.0 - staminaRatio) * 100);
    }
}

public sealed record SquadSwapResult(bool Success, string Message)
{
    public static SquadSwapResult Succeeded() => new(true, string.Empty);

    public static SquadSwapResult Failed(string message) => new(false, message);
}
