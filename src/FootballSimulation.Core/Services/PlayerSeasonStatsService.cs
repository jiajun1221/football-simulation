using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class PlayerSeasonStatsService
{
    private const int FullMatchMinutes = 90;

    public List<PlayerSeasonStats> RebuildSeasonStats(League league)
    {
        ArgumentNullException.ThrowIfNull(league);

        var playerLookup = league.Teams
            .SelectMany(team => team.Players.Concat(team.Substitutes).Select(player =>
                new PlayerLookupEntry(team, player, CreatePlayerId(team, player))))
            .GroupBy(item => CreateLookupKey(item.Team.Name, item.Player.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var builders = new Dictionary<string, SeasonStatsBuilder>(StringComparer.OrdinalIgnoreCase);
        foreach (var fixture in league.Fixtures.Where(fixture => fixture.IsPlayed && fixture.Result is not null))
        {
            ApplyMatch(fixture.Result!, playerLookup, builders);
        }

        return builders.Values
            .Select(builder => builder.ToStats())
            .OrderBy(stat => stat.TeamName)
            .ThenBy(stat => stat.PlayerName)
            .ToList();
    }

    public void RebuildLeagueSeasonStats(League league)
    {
        league.PlayerStats = RebuildSeasonStats(league);
    }

    private static void ApplyMatch(
        Match match,
        IReadOnlyDictionary<string, PlayerLookupEntry> playerLookup,
        Dictionary<string, SeasonStatsBuilder> builders)
    {
        foreach (var performance in match.PlayerPerformances)
        {
            var lookupKey = CreateLookupKey(performance.TeamName, performance.PlayerName);
            if (!playerLookup.TryGetValue(lookupKey, out var playerEntry))
            {
                continue;
            }

            var builder = GetOrCreateBuilder(builders, playerEntry.Team, playerEntry.Player, playerEntry.PlayerId);
            var goalsConceded = string.Equals(performance.TeamName, match.HomeTeam.Name, StringComparison.OrdinalIgnoreCase)
                ? match.AwayScore
                : match.HomeScore;

            builder.Appearances++;
            if (!performance.WasSubstitute || performance.WasSubbedOff)
            {
                builder.Starts++;
            }

            builder.Goals += performance.Goals;
            builder.Assists += performance.Assists;
            builder.Saves += performance.Saves;
            builder.YellowCards += performance.YellowCards;
            builder.RedCards += performance.RedCards;
            builder.RatingTotal += performance.Rating;
            builder.MinutesPlayed += EstimateMinutesPlayed(performance);

            if (ShouldCreditGoalsConceded(performance.Position))
            {
                builder.GoalsConceded += goalsConceded;
                if (goalsConceded == 0)
                {
                    builder.CleanSheets++;
                }
            }
        }
    }

    private static SeasonStatsBuilder GetOrCreateBuilder(
        Dictionary<string, SeasonStatsBuilder> builders,
        Team team,
        Player player,
        string playerId)
    {
        if (builders.TryGetValue(playerId, out var builder))
        {
            return builder;
        }

        builder = new SeasonStatsBuilder(playerId, player, team);
        builders[playerId] = builder;
        return builder;
    }

    private static int EstimateMinutesPlayed(PlayerMatchPerformance performance)
    {
        if (performance.WasSubbedOn && performance.SubstitutionMinute is int subOnMinute)
        {
            return Math.Clamp(FullMatchMinutes - subOnMinute + 1, 1, FullMatchMinutes);
        }

        if (performance.WasSubbedOff && performance.SubstitutionMinute is int subOffMinute)
        {
            return Math.Clamp(subOffMinute, 1, FullMatchMinutes);
        }

        return FullMatchMinutes;
    }

    private static bool ShouldCreditGoalsConceded(Position position)
    {
        return position is Position.Goalkeeper or Position.Defender;
    }

    public static string CreatePlayerId(Team team, Player player)
    {
        var squadNumber = player.SquadNumber > 0 ? player.SquadNumber.ToString() : "no-number";
        return $"{team.Name}|{player.Name}|{squadNumber}";
    }

    private static string CreateLookupKey(string teamName, string playerName)
    {
        return $"{teamName}|{playerName}";
    }

    private sealed record PlayerLookupEntry(Team Team, Player Player, string PlayerId);

    private sealed class SeasonStatsBuilder
    {
        private readonly Player _player;
        private readonly Team _team;

        public SeasonStatsBuilder(string playerId, Player player, Team team)
        {
            PlayerId = playerId;
            _player = player;
            _team = team;
        }

        public string PlayerId { get; }
        public int Appearances { get; set; }
        public int Starts { get; set; }
        public int Goals { get; set; }
        public int Assists { get; set; }
        public int Saves { get; set; }
        public int GoalsConceded { get; set; }
        public int CleanSheets { get; set; }
        public int YellowCards { get; set; }
        public int RedCards { get; set; }
        public double RatingTotal { get; set; }
        public int MinutesPlayed { get; set; }

        public PlayerSeasonStats ToStats()
        {
            return new PlayerSeasonStats
            {
                PlayerId = PlayerId,
                PlayerName = _player.Name,
                TeamId = _team.Name,
                TeamName = _team.Name,
                Position = _player.Position,
                ExactPosition = GetExactPosition(_player),
                Appearances = Appearances,
                Starts = Starts,
                Goals = Goals,
                Assists = Assists,
                Saves = Saves,
                GoalsConceded = GoalsConceded,
                CleanSheets = CleanSheets,
                YellowCards = YellowCards,
                RedCards = RedCards,
                AverageRating = Appearances == 0 ? 0 : Math.Round(RatingTotal / Appearances, 2),
                MinutesPlayed = MinutesPlayed
            };
        }
    }

    private static string GetExactPosition(Player player)
    {
        var preferredPosition = PositionSuitabilityService.NormalizeExactPosition(player.PreferredPosition);
        if (!string.IsNullOrWhiteSpace(preferredPosition))
        {
            return preferredPosition;
        }

        var assignedPosition = PositionSuitabilityService.NormalizeExactPosition(player.AssignedPosition);
        if (!string.IsNullOrWhiteSpace(assignedPosition))
        {
            return assignedPosition;
        }

        return PositionSuitabilityService.GetDefaultExactPosition(player.Position);
    }
}
