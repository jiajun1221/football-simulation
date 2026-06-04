using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class SeasonAwardsService
{
    private static readonly string[] BestXiSlots =
    [
        "GK",
        "RB",
        "CB",
        "CB",
        "LB",
        "CM",
        "CM",
        "CAM",
        "RW",
        "ST",
        "LW"
    ];

    public SeasonArchive CreateArchive(League league, Team selectedTeam)
    {
        ArgumentNullException.ThrowIfNull(league);
        ArgumentNullException.ThrowIfNull(selectedTeam);

        var sortedTable = new LeagueTableService().SortTable(league.Table);
        var archivedStats = CreateArchivedPlayerStats(league.PlayerStats.Count > 0
            ? league.PlayerStats
            : new PlayerSeasonStatsService().RebuildSeasonStats(league));
        var selectedClubPosition = GetPosition(sortedTable, selectedTeam.Name);

        return new SeasonArchive
        {
            Season = league.Season,
            LeagueId = league.LeagueId,
            LeagueName = league.Name,
            CompletedAt = DateTime.Now,
            SelectedClubName = selectedTeam.Name,
            SelectedClubPosition = selectedClubPosition,
            SelectedClubOutcome = GetOutcomeLabel(selectedClubPosition, sortedTable.Count),
            FinalTable = CreateFinalTableRows(sortedTable),
            PlayerStats = archivedStats,
            Awards = CreateAwards(league, archivedStats),
            Highlights = CreateHighlights(league, sortedTable, archivedStats)
        };
    }

    public SeasonAwards CreateAwards(League league, IReadOnlyList<ArchivedPlayerStatRow> stats)
    {
        var matchesPerTeam = Math.Max(1, league.Teams.Count - 1) * 2;
        var minimumAppearances = Math.Max(1, (int)Math.Ceiling(matchesPerTeam * 0.5));
        var scoredPlayers = stats
            .Where(stat => stat.Appearances >= minimumAppearances)
            .Select(stat => new
            {
                Stat = stat,
                Score = CalculatePlayerOfSeasonScore(stat)
            })
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Stat.AverageRating)
            .ThenByDescending(item => item.Stat.Goals + item.Stat.Assists)
            .ToList();

        var playerOfSeason = scoredPlayers.FirstOrDefault();
        var youngPlayer = scoredPlayers
            .Where(item => FindPlayer(league, item.Stat.PlayerId, item.Stat.PlayerName)?.Age <= 23)
            .FirstOrDefault();

        return new SeasonAwards
        {
            PlayerOfTheSeason = playerOfSeason is null
                ? new SeasonAwardWinner { AwardName = "Player of the Season", Summary = "No eligible players." }
                : CreateAwardWinner("Player of the Season", playerOfSeason.Stat, playerOfSeason.Score),
            YoungPlayerOfTheSeason = youngPlayer is null
                ? new SeasonAwardWinner { AwardName = "Young Player of the Season", Summary = "No eligible players." }
                : CreateAwardWinner("Young Player of the Season", youngPlayer.Stat, youngPlayer.Score),
            BestXi = CreateBestXi(league, stats)
        };
    }

    public List<SeasonHighlight> CreateHighlights(
        League league,
        IReadOnlyList<LeagueTableEntry> sortedTable,
        IReadOnlyList<ArchivedPlayerStatRow> stats)
    {
        var highlights = new List<SeasonHighlight>();
        var playedMatches = league.Fixtures
            .Where(fixture => fixture.IsPlayed && fixture.Result is not null)
            .Select(fixture => fixture.Result!)
            .ToList();

        var biggestWin = playedMatches
            .Select(match => new
            {
                Match = match,
                Margin = Math.Abs(match.HomeScore - match.AwayScore),
                Winner = match.HomeScore >= match.AwayScore ? match.HomeTeam.Name : match.AwayTeam.Name
            })
            .Where(item => item.Margin > 0)
            .OrderByDescending(item => item.Margin)
            .ThenByDescending(item => item.Match.HomeScore + item.Match.AwayScore)
            .FirstOrDefault();
        if (biggestWin is not null)
        {
            highlights.Add(new SeasonHighlight
            {
                Icon = "WIN",
                Title = "Biggest Win",
                PrimaryText = $"{biggestWin.Match.HomeTeam.Name} {biggestWin.Match.HomeScore}-{biggestWin.Match.AwayScore} {biggestWin.Match.AwayTeam.Name}",
                SecondaryText = $"{biggestWin.Winner} won by {biggestWin.Margin}."
            });
        }

        var highestScoring = playedMatches
            .OrderByDescending(match => match.HomeScore + match.AwayScore)
            .FirstOrDefault();
        if (highestScoring is not null)
        {
            highlights.Add(new SeasonHighlight
            {
                Icon = "GOAL",
                Title = "Highest Scoring Match",
                PrimaryText = $"{highestScoring.HomeTeam.Name} {highestScoring.HomeScore}-{highestScoring.AwayScore} {highestScoring.AwayTeam.Name}",
                SecondaryText = $"{highestScoring.HomeScore + highestScoring.AwayScore} total goals."
            });
        }

        AddPlayerLeader(highlights, stats, "Top Scorer", "GOLDEN BOOT", stat => stat.Goals, "goals");
        AddPlayerLeader(highlights, stats, "Assist King", "ASSIST", stat => stat.Assists, "assists");
        AddPlayerLeader(highlights, stats, "Best Goalkeeper", "GLOVE", stat => stat.Saves + stat.CleanSheets * 4, "goalkeeper score");

        var worstDisciplinaryTeam = stats
            .GroupBy(stat => stat.TeamName)
            .Select(group => new
            {
                TeamName = group.Key,
                Points = group.Sum(stat => stat.YellowCards + stat.RedCards * 3)
            })
            .OrderByDescending(item => item.Points)
            .FirstOrDefault();
        if (worstDisciplinaryTeam is not null && worstDisciplinaryTeam.Points > 0)
        {
            highlights.Add(new SeasonHighlight
            {
                Icon = "CARDS",
                Title = "Disciplinary Watch",
                PrimaryText = worstDisciplinaryTeam.TeamName,
                SecondaryText = $"{worstDisciplinaryTeam.Points} disciplinary points."
            });
        }

        var lateWin = playedMatches
            .Where(match => match.HomeScore != match.AwayScore)
            .Select(match => new
            {
                Match = match,
                LateEvent = match.Events
                    .Where(matchEvent => matchEvent.Minute >= 85 && matchEvent.HomeScore is not null && matchEvent.AwayScore is not null)
                    .OrderByDescending(matchEvent => matchEvent.Minute)
                    .FirstOrDefault()
            })
            .Where(item => item.LateEvent is not null)
            .OrderByDescending(item => item.LateEvent!.Minute)
            .FirstOrDefault();
        if (lateWin is not null)
        {
            highlights.Add(new SeasonHighlight
            {
                Icon = "LATE",
                Title = "Late Drama",
                PrimaryText = $"{lateWin.Match.HomeTeam.Name} {lateWin.Match.HomeScore}-{lateWin.Match.AwayScore} {lateWin.Match.AwayTeam.Name}",
                SecondaryText = $"Decisive moment in minute {lateWin.LateEvent!.Minute}."
            });
        }

        var surpriseTeam = GetSurpriseTeam(league, sortedTable);
        if (surpriseTeam is not null)
        {
            highlights.Add(new SeasonHighlight
            {
                Icon = "RISE",
                Title = "Surprise Team",
                PrimaryText = surpriseTeam.Value.TeamName,
                SecondaryText = $"Finished {GetOrdinal(surpriseTeam.Value.Position)} despite a modest squad rating."
            });
        }

        return highlights;
    }

    private static List<ArchivedLeagueTableRow> CreateFinalTableRows(IReadOnlyList<LeagueTableEntry> sortedTable)
    {
        return sortedTable
            .Select((entry, index) => new ArchivedLeagueTableRow
            {
                Position = index + 1,
                TeamName = entry.TeamName,
                Played = entry.Played,
                Wins = entry.Wins,
                Draws = entry.Draws,
                Losses = entry.Losses,
                GoalsFor = entry.GoalsFor,
                GoalsAgainst = entry.GoalsAgainst,
                GoalDifference = entry.GoalDifference,
                Points = entry.Points
            })
            .ToList();
    }

    private static List<ArchivedPlayerStatRow> CreateArchivedPlayerStats(IEnumerable<PlayerSeasonStats> stats)
    {
        return stats
            .Select(stat => new ArchivedPlayerStatRow
            {
                PlayerId = stat.PlayerId,
                PlayerName = stat.PlayerName,
                TeamName = stat.TeamName,
                Position = stat.Position,
                ExactPosition = stat.ExactPosition,
                Appearances = stat.Appearances,
                Goals = stat.Goals,
                Assists = stat.Assists,
                Saves = stat.Saves,
                CleanSheets = stat.CleanSheets,
                YellowCards = stat.YellowCards,
                RedCards = stat.RedCards,
                AverageRating = stat.AverageRating,
                MinutesPlayed = stat.MinutesPlayed
            })
            .OrderByDescending(stat => stat.AverageRating)
            .ThenByDescending(stat => stat.Goals + stat.Assists)
            .ToList();
    }

    private static List<BestXiPlayer> CreateBestXi(League league, IReadOnlyList<ArchivedPlayerStatRow> stats)
    {
        var selectedPlayerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bestXi = new List<BestXiPlayer>();

        foreach (var slot in BestXiSlots)
        {
            var candidate = stats
                .Where(stat => !selectedPlayerIds.Contains(stat.PlayerId))
                .Select(stat => new
                {
                    Stat = stat,
                    Suitability = GetSlotSuitability(stat, slot),
                    Score = CalculateBestXiScore(stat)
                })
                .Where(item => item.Suitability > 0)
                .OrderByDescending(item => item.Suitability)
                .ThenByDescending(item => item.Score)
                .ThenByDescending(item => item.Stat.AverageRating)
                .FirstOrDefault();

            if (candidate is null)
            {
                continue;
            }

            selectedPlayerIds.Add(candidate.Stat.PlayerId);
            var player = FindPlayer(league, candidate.Stat.PlayerId, candidate.Stat.PlayerName);
            bestXi.Add(new BestXiPlayer
            {
                Slot = slot,
                PlayerName = candidate.Stat.PlayerName,
                TeamName = candidate.Stat.TeamName,
                Position = string.IsNullOrWhiteSpace(candidate.Stat.ExactPosition) ? slot : candidate.Stat.ExactPosition,
                OverallRating = player?.OverallRating ?? 0,
                AverageRating = candidate.Stat.AverageRating,
                Goals = candidate.Stat.Goals,
                Assists = candidate.Stat.Assists,
                Saves = candidate.Stat.Saves
            });
        }

        return bestXi;
    }

    private static double CalculatePlayerOfSeasonScore(ArchivedPlayerStatRow stat)
    {
        var attackingScore = stat.Goals * 0.55 + stat.Assists * 0.4;
        var goalkeeperScore = stat.Position == Position.Goalkeeper
            ? stat.Saves * 0.08 + stat.CleanSheets * 0.8
            : 0;
        var defensiveScore = stat.Position == Position.Defender
            ? stat.CleanSheets * 0.6
            : stat.CleanSheets * 0.2;

        return stat.AverageRating * 12 +
            attackingScore +
            goalkeeperScore +
            defensiveScore +
            stat.Appearances * 0.08;
    }

    private static double CalculateBestXiScore(ArchivedPlayerStatRow stat)
    {
        return stat.AverageRating * 10 +
            stat.Goals * 0.5 +
            stat.Assists * 0.35 +
            stat.CleanSheets * 0.45 +
            stat.Saves * 0.04 +
            stat.Appearances * 0.05;
    }

    private static int GetSlotSuitability(ArchivedPlayerStatRow stat, string slot)
    {
        var exactPosition = PositionSuitabilityService.NormalizeExactPosition(stat.ExactPosition);
        if (exactPosition.Equals(slot, StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        return slot switch
        {
            "GK" => stat.Position == Position.Goalkeeper ? 90 : 0,
            "CB" => stat.Position == Position.Defender ? 85 : 0,
            "LB" or "RB" => exactPosition is "LB" or "RB" ? 95 : stat.Position == Position.Defender ? 70 : 0,
            "CM" => exactPosition is "CM" or "CDM" or "CAM" ? 90 : stat.Position == Position.Midfielder ? 75 : 0,
            "CAM" => exactPosition is "CAM" or "CM" or "CDM" ? 90 : stat.Position == Position.Midfielder ? 75 : 0,
            "LW" or "RW" => exactPosition is "LW" or "RW" ? 95 : stat.Position == Position.Forward ? 70 : 0,
            "ST" => exactPosition == "ST" ? 100 : stat.Position == Position.Forward ? 80 : 0,
            _ => 0
        };
    }

    private static SeasonAwardWinner CreateAwardWinner(string awardName, ArchivedPlayerStatRow stat, double score)
    {
        return new SeasonAwardWinner
        {
            AwardName = awardName,
            PlayerName = stat.PlayerName,
            TeamName = stat.TeamName,
            Position = string.IsNullOrWhiteSpace(stat.ExactPosition) ? stat.Position.ToString() : stat.ExactPosition,
            Score = Math.Round(score, 1),
            Summary = $"{stat.AverageRating:0.00} rating, {stat.Goals} goals, {stat.Assists} assists."
        };
    }

    private static Player? FindPlayer(League league, string playerId, string playerName)
    {
        return league.Teams
            .SelectMany(team => team.Players.Concat(team.Substitutes))
            .FirstOrDefault(player => !string.IsNullOrWhiteSpace(playerId) &&
                    player.PlayerId.Equals(playerId, StringComparison.OrdinalIgnoreCase)) ??
            league.Teams
                .SelectMany(team => team.Players.Concat(team.Substitutes))
                .FirstOrDefault(player => player.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddPlayerLeader(
        List<SeasonHighlight> highlights,
        IReadOnlyList<ArchivedPlayerStatRow> stats,
        string title,
        string icon,
        Func<ArchivedPlayerStatRow, int> selector,
        string suffix)
    {
        var leader = stats
            .OrderByDescending(selector)
            .ThenByDescending(stat => stat.AverageRating)
            .FirstOrDefault();
        if (leader is null || selector(leader) <= 0)
        {
            return;
        }

        highlights.Add(new SeasonHighlight
        {
            Icon = icon,
            Title = title,
            PrimaryText = leader.PlayerName,
            SecondaryText = $"{leader.TeamName} - {selector(leader)} {suffix}."
        });
    }

    private static (string TeamName, int Position)? GetSurpriseTeam(
        League league,
        IReadOnlyList<LeagueTableEntry> sortedTable)
    {
        var strengthRanks = league.Teams
            .Select(team => new
            {
                TeamName = team.Name,
                Strength = team.Players.Concat(team.Substitutes).DefaultIfEmpty().Average(player => player?.OverallRating ?? 70)
            })
            .OrderByDescending(item => item.Strength)
            .Select((item, index) => new { item.TeamName, StrengthRank = index + 1 })
            .ToDictionary(item => item.TeamName, item => item.StrengthRank, StringComparer.OrdinalIgnoreCase);

        return sortedTable
            .Select((entry, index) => new
            {
                entry.TeamName,
                Position = index + 1,
                SurpriseScore = strengthRanks.GetValueOrDefault(entry.TeamName, index + 1) - (index + 1)
            })
            .Where(item => item.SurpriseScore >= 4)
            .OrderByDescending(item => item.SurpriseScore)
            .Select(item => ((string TeamName, int Position)?)(item.TeamName, item.Position))
            .FirstOrDefault();
    }

    private static int GetPosition(IReadOnlyList<LeagueTableEntry> sortedTable, string teamName)
    {
        var index = sortedTable
            .Select((entry, index) => new { entry.TeamName, Index = index })
            .FirstOrDefault(item => item.TeamName.Equals(teamName, StringComparison.OrdinalIgnoreCase))
            ?.Index;
        return index is null ? 0 : index.Value + 1;
    }

    private static string GetOutcomeLabel(int position, int teamCount)
    {
        if (position <= 0)
        {
            return "Season Finished";
        }

        if (position == 1)
        {
            return "Champions";
        }

        if (position <= 4)
        {
            return "Qualified for Champions League";
        }

        if (position == 5)
        {
            return "Qualified for Europa League";
        }

        if (position == 6)
        {
            return "Qualified for Conference League";
        }

        if (position > teamCount - 3)
        {
            return "Relegated";
        }

        return "Season Finished";
    }

    private static string GetOrdinal(int value)
    {
        var suffix = value % 100 is 11 or 12 or 13
            ? "th"
            : (value % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            };
        return $"{value}{suffix}";
    }
}
