using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class LineupValidationService
{
    public const string NoAvailableGoalkeeperMessage = "Club has no available goalkeeper";

    public static LineupValidationResult RepairUnavailablePlayers(Team team)
    {
        ArgumentNullException.ThrowIfNull(team);

        var unavailableStarters = team.Players
            .Where(player => !IsAvailableForSelection(player))
            .ToList();

        var wasRepaired = false;
        foreach (var unavailableStarter in unavailableStarters)
        {
            var replacement = team.Substitutes
                .Where(IsAvailableForSelection)
                .OrderByDescending(player => PositionSuitabilityService.IsGoalkeeperCapable(unavailableStarter) == PositionSuitabilityService.IsGoalkeeperCapable(player))
                .ThenByDescending(player => player.OverallRating)
                .ThenBy(player => player.SquadNumber <= 0 ? int.MaxValue : player.SquadNumber)
                .FirstOrDefault();

            if (replacement is null)
            {
                team.Players.Remove(unavailableStarter);
                if (!team.Substitutes.Contains(unavailableStarter))
                {
                    team.Substitutes.Add(unavailableStarter);
                }

                unavailableStarter.IsStarter = false;
                unavailableStarter.IsOnPitch = false;
                wasRepaired = true;
                continue;
            }

            SwapStarterWithBenchPlayer(team, unavailableStarter, replacement);
            wasRepaired = true;
        }

        while (team.Players.Count < 11)
        {
            var replacement = team.Substitutes
                .Where(IsAvailableForSelection)
                .OrderByDescending(player => player.OverallRating)
                .ThenBy(player => player.SquadNumber <= 0 ? int.MaxValue : player.SquadNumber)
                .FirstOrDefault();

            if (replacement is null)
            {
                break;
            }

            team.Substitutes.Remove(replacement);
            team.Players.Add(replacement);
            replacement.IsStarter = true;
            replacement.IsOnPitch = true;
            wasRepaired = true;
        }

        var goalkeeperResult = RepairGoalkeeperSlot(team);
        if (!goalkeeperResult.IsValid)
        {
            return goalkeeperResult;
        }

        return wasRepaired || goalkeeperResult.WasRepaired
            ? LineupValidationResult.Repaired()
            : LineupValidationResult.Valid();
    }

    public static LineupValidationResult RepairGoalkeeperSlot(Team team)
    {
        ArgumentNullException.ThrowIfNull(team);

        var startingGoalkeepers = team.Players
            .Where(PositionSuitabilityService.IsGoalkeeperCapable)
            .ToList();

        if (startingGoalkeepers.Count == 1)
        {
            PositionSuitabilityService.EnsurePositionMetadata(startingGoalkeepers[0], "GK");
            return LineupValidationResult.Valid();
        }

        if (startingGoalkeepers.Count > 1)
        {
            KeepSingleStartingGoalkeeper(team, startingGoalkeepers);
            return LineupValidationResult.Repaired();
        }

        var backupGoalkeeper = team.Substitutes
            .Where(IsAvailableForSelection)
            .Where(PositionSuitabilityService.IsGoalkeeperCapable)
            .OrderByDescending(player => player.OverallRating)
            .ThenBy(player => player.SquadNumber <= 0 ? int.MaxValue : player.SquadNumber)
            .FirstOrDefault();

        if (backupGoalkeeper is null)
        {
            return LineupValidationResult.Invalid(NoAvailableGoalkeeperMessage);
        }

        PromoteBackupGoalkeeper(team, backupGoalkeeper);
        return LineupValidationResult.Repaired();
    }

    private static void KeepSingleStartingGoalkeeper(Team team, IReadOnlyList<Player> startingGoalkeepers)
    {
        var keeperToKeep = startingGoalkeepers
            .OrderByDescending(player => player.OverallRating)
            .ThenBy(player => player.SquadNumber <= 0 ? int.MaxValue : player.SquadNumber)
            .First();

        PositionSuitabilityService.EnsurePositionMetadata(keeperToKeep, "GK");

        foreach (var extraGoalkeeper in startingGoalkeepers.Where(player => player != keeperToKeep).ToList())
        {
            var replacement = team.Substitutes
                .Where(IsAvailableForSelection)
                .Where(player => !PositionSuitabilityService.IsGoalkeeperCapable(player))
                .OrderByDescending(player => player.OverallRating)
                .ThenBy(player => player.SquadNumber <= 0 ? int.MaxValue : player.SquadNumber)
                .FirstOrDefault();

            if (replacement is null)
            {
                continue;
            }

            SwapStarterWithBenchPlayer(team, extraGoalkeeper, replacement);
        }
    }

    private static void PromoteBackupGoalkeeper(Team team, Player backupGoalkeeper)
    {
        var playerToReplace = team.Players
            .Where(player => !PositionSuitabilityService.IsGoalkeeperCapable(player))
            .OrderBy(player => player.OverallRating)
            .ThenByDescending(player => player.SquadNumber <= 0 ? int.MaxValue : player.SquadNumber)
            .FirstOrDefault() ?? team.Players.FirstOrDefault();

        if (playerToReplace is null)
        {
            team.Substitutes.Remove(backupGoalkeeper);
            team.Players.Add(backupGoalkeeper);
            backupGoalkeeper.IsStarter = true;
            backupGoalkeeper.IsOnPitch = true;
            PositionSuitabilityService.EnsurePositionMetadata(backupGoalkeeper, "GK");
            return;
        }

        SwapStarterWithBenchPlayer(team, playerToReplace, backupGoalkeeper);
        PositionSuitabilityService.EnsurePositionMetadata(backupGoalkeeper, "GK");
    }

    private static void SwapStarterWithBenchPlayer(Team team, Player starter, Player substitute)
    {
        var starterIndex = team.Players.IndexOf(starter);
        if (starterIndex < 0)
        {
            return;
        }

        team.Players[starterIndex] = substitute;
        team.Substitutes.Remove(substitute);
        if (!team.Substitutes.Contains(starter))
        {
            team.Substitutes.Add(starter);
        }

        starter.IsStarter = false;
        starter.IsOnPitch = false;
        substitute.IsStarter = true;
        substitute.IsOnPitch = true;
        PositionSuitabilityService.EnsurePositionMetadata(starter);
    }

    private static bool IsAvailableForSelection(Player player)
    {
        return !player.IsInjured && !player.IsSuspended && !player.IsSentOff;
    }
}

public sealed record LineupValidationResult(bool IsValid, bool WasRepaired, string? Message)
{
    public static LineupValidationResult Valid() => new(true, false, null);

    public static LineupValidationResult Repaired() => new(true, true, null);

    public static LineupValidationResult Invalid(string message) => new(false, false, message);
}
