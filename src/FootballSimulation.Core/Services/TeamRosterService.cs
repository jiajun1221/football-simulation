using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class TeamRosterService
{
    public const int MatchdaySubstituteCount = 8;

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
        var available = candidates.Where(IsAvailable).ToList();
        var selected = new List<Player>();

        AddBest(selected, available, player => player.Position == Position.Goalkeeper, 1);
        AddBest(selected, available, player => player.Position == Position.Defender, 3);
        AddBest(selected, available, player => player.Position == Position.Midfielder, 5);
        AddBest(selected, available, player => player.Position == Position.Forward, 7);
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

    private static bool IsAvailable(Player player)
    {
        return !player.IsInjured && !player.IsSuspended && !player.IsSentOff;
    }
}
