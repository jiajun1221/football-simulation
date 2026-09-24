using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class TeamRosterService
{
    public const int MatchdaySubstituteCount = 8;
    private static readonly string[][] MatchdayBenchRoles =
    [
        ["ST"],
        ["LW"],
        ["RW"],
        ["CAM"],
        ["CB"],
        ["LB"],
        ["RB"],
        ["CM", "CDM"]
    ];

    public static IEnumerable<Player> GetAllPlayers(Team team)
    {
        ArgumentNullException.ThrowIfNull(team);
        return team.Players.Concat(team.Substitutes).Concat(team.Reserves);
    }

    public static List<Player> GetDistinctPlayers(Team team)
    {
        return GetAllPlayers(team)
            .GroupBy(CreatePlayerKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    public static void SelectMatchdayBench(Team team)
    {
        ArgumentNullException.ThrowIfNull(team);

        var starterKeys = team.Players.Select(CreatePlayerKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = GetDistinctPlayers(team)
            .Where(player => !starterKeys.Contains(CreatePlayerKey(player)))
            .ToList();
        var available = candidates
            .Where(IsAvailable)
            .Where(player => !PositionSuitabilityService.IsGoalkeeperCapable(player))
            .ToList();
        var selected = new List<Player>();

        foreach (var roles in MatchdayBenchRoles)
        {
            AddBestForRole(selected, available, roles);
        }

        AddBest(selected, available, _ => true, MatchdaySubstituteCount);

        var selectedKeys = selected.Select(CreatePlayerKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var player in candidates)
        {
            player.IsStarter = false;
            player.IsOnPitch = false;
        }

        team.Substitutes = selected.Take(MatchdaySubstituteCount).ToList();
        team.Reserves = candidates
            .Where(player => !selectedKeys.Contains(CreatePlayerKey(player)))
            .OrderByDescending(IsAvailable)
            .ThenByDescending(player => player.OverallRating)
            .ThenBy(player => player.SquadNumber <= 0 ? int.MaxValue : player.SquadNumber)
            .ToList();
    }

    public static Player? PromoteReserveGoalkeeperForInjury(Team team)
    {
        ArgumentNullException.ThrowIfNull(team);

        var benchGoalkeeper = team.Substitutes.FirstOrDefault(player =>
            IsAvailable(player) && PositionSuitabilityService.IsGoalkeeperCapable(player));
        if (benchGoalkeeper is not null)
        {
            return benchGoalkeeper;
        }

        var reserveGoalkeeper = team.Reserves
            .Where(IsAvailable)
            .Where(PositionSuitabilityService.IsGoalkeeperCapable)
            .OrderByDescending(player => player.OverallRating)
            .ThenBy(player => player.SquadNumber <= 0 ? int.MaxValue : player.SquadNumber)
            .FirstOrDefault();
        if (reserveGoalkeeper is null)
        {
            return null;
        }

        if (team.Substitutes.Count >= MatchdaySubstituteCount)
        {
            var playerToReserve = team.Substitutes
                .Where(player => !PositionSuitabilityService.IsGoalkeeperCapable(player))
                .OrderBy(player => player.OverallRating)
                .ThenByDescending(player => player.SquadNumber)
                .FirstOrDefault();
            if (playerToReserve is not null)
            {
                team.Substitutes.Remove(playerToReserve);
                team.Reserves.Add(playerToReserve);
            }
        }

        team.Reserves.Remove(reserveGoalkeeper);
        team.Substitutes.Add(reserveGoalkeeper);
        return reserveGoalkeeper;
    }

    public static void MoveToReserves(Team team, Player player)
    {
        team.Players.Remove(player);
        team.Substitutes.Remove(player);
        if (!team.Reserves.Any(existing => CreatePlayerKey(existing).Equals(CreatePlayerKey(player), StringComparison.OrdinalIgnoreCase)))
        {
            player.IsStarter = false;
            player.IsOnPitch = false;
            team.Reserves.Add(player);
        }
    }

    public static string CreatePlayerKey(Player player)
    {
        return !string.IsNullOrWhiteSpace(player.PlayerId)
            ? player.PlayerId
            : $"{player.Name}|{player.NationalityCode}|{player.Age}";
    }

    private static void AddBest(
        ICollection<Player> selected,
        IEnumerable<Player> candidates,
        Func<Player, bool> predicate,
        int targetCount)
    {
        var used = selected.Select(CreatePlayerKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var player in candidates
                     .Where(predicate)
                     .Where(player => !used.Contains(CreatePlayerKey(player)))
                     .OrderByDescending(IsAvailable)
                     .ThenByDescending(player => player.OverallRating)
                     .ThenBy(player => player.SquadNumber <= 0 ? int.MaxValue : player.SquadNumber))
        {
            selected.Add(player);
            used.Add(CreatePlayerKey(player));
            if (selected.Count >= targetCount)
            {
                return;
            }
        }
    }

    private static void AddBestForRole(
        ICollection<Player> selected,
        IEnumerable<Player> candidates,
        IReadOnlyCollection<string> roles)
    {
        var used = selected.Select(CreatePlayerKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var player = candidates
            .Where(candidate => !used.Contains(CreatePlayerKey(candidate)))
            .Where(candidate => PositionSuitabilityService.GetNaturalExactPositions(candidate)
                .Any(position => roles.Contains(position, StringComparer.OrdinalIgnoreCase)))
            .OrderByDescending(candidate => candidate.OverallRating)
            .ThenBy(candidate => candidate.SquadNumber <= 0 ? int.MaxValue : candidate.SquadNumber)
            .FirstOrDefault();
        if (player is not null)
        {
            selected.Add(player);
        }
    }

    private static bool IsAvailable(Player player)
    {
        return !player.IsInjured && !player.IsSuspended && !player.IsSentOff;
    }
}
