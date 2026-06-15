using System.Text.Json;
using FootballSimulation.Data.JsonModels;
using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class PlayerFormPersistenceService
{
    private const string SquadIndexFileName = "squad-data-index.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public void SaveActiveSquadFormStatuses(IEnumerable<Team> teams)
    {
        SaveActiveSquadFormStatuses(teams, LeagueDataService.DefaultLeagueId);
    }

    public void SaveActiveSquadFormStatuses(IEnumerable<Team> teams, string? leagueId)
    {
        var outputDataFolder = Path.Combine(AppContext.BaseDirectory, "Data", "Json");
        var activeSquadFileName = GetActiveSquadFileName(outputDataFolder, leagueId);
        if (string.IsNullOrWhiteSpace(activeSquadFileName))
        {
            return;
        }

        foreach (var dataFolder in GetWritableDataFolders(outputDataFolder).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            SaveSquadFormStatuses(Path.Combine(dataFolder, activeSquadFileName), teams);
        }
    }

    private static string? GetActiveSquadFileName(string dataFolder, string? leagueId)
    {
        var leagueIndexPath = Path.Combine(dataFolder, "leagues-index.json");
        if (File.Exists(leagueIndexPath))
        {
            var leagueIndex = JsonSerializer.Deserialize<LeagueDataIndex>(File.ReadAllText(leagueIndexPath), JsonOptions);
            var normalizedLeagueId = string.IsNullOrWhiteSpace(leagueId)
                ? LeagueDataService.DefaultLeagueId
                : leagueId.Trim();
            var definition = leagueIndex?.Leagues.FirstOrDefault(league =>
                string.Equals(league.LeagueId, normalizedLeagueId, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(definition?.SquadFile))
            {
                return definition.SquadFile;
            }
        }

        var indexPath = Path.Combine(dataFolder, SquadIndexFileName);
        if (!File.Exists(indexPath))
        {
            return null;
        }

        var index = JsonSerializer.Deserialize<SquadDataIndex>(File.ReadAllText(indexPath), JsonOptions);
        return index?.ActiveSquadFile;
    }

    private static IEnumerable<string> GetWritableDataFolders(string outputDataFolder)
    {
        yield return outputDataFolder;

        var sourceDataFolder = FindSourceDataFolder(AppContext.BaseDirectory);
        if (!string.IsNullOrWhiteSpace(sourceDataFolder))
        {
            yield return sourceDataFolder;
        }
    }

    private static string? FindSourceDataFolder(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "FootballSimulation.Core", "Data", "Json");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static void SaveSquadFormStatuses(string squadFilePath, IEnumerable<Team> teams)
    {
        if (!File.Exists(squadFilePath))
        {
            return;
        }

        var squadsFile = JsonSerializer.Deserialize<PremierLeagueSquadsFile>(
            File.ReadAllText(squadFilePath),
            JsonOptions);

        if (squadsFile is null)
        {
            return;
        }

        var teamsByName = teams.ToDictionary(team => team.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var teamRecord in squadsFile.Teams)
        {
            if (!teamsByName.TryGetValue(teamRecord.Name, out var team))
            {
                continue;
            }

            var playersByName = team.Players
                .Concat(team.Substitutes)
                .GroupBy(player => player.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            UpdatePlayerRecords(teamRecord.StartingXI, playersByName);
            UpdatePlayerRecords(teamRecord.Substitutes, playersByName);
        }

        File.WriteAllText(squadFilePath, JsonSerializer.Serialize(squadsFile, JsonOptions));
    }

    private static void UpdatePlayerRecords(
        IEnumerable<SquadPlayerRecord> playerRecords,
        IReadOnlyDictionary<string, Player> playersByName)
    {
        foreach (var record in playerRecords)
        {
            if (!playersByName.TryGetValue(record.Name, out var player))
            {
                continue;
            }

            record.IsInjured = player.IsInjured;
            record.InjuryType = string.IsNullOrWhiteSpace(player.InjuryType) ? null : player.InjuryType;
            record.InjurySeverity = player.InjurySeverity?.ToString();
            record.InjuryRecoveryMatches = player.IsInjured ? player.InjuryRecoveryMatches : null;
            record.IsSeasonEndingInjury = player.IsSeasonEndingInjury ? true : null;
            record.MatchesPlayedRecently = player.MatchesPlayedRecently;
            record.RecentMatchMinutes = player.RecentMatchMinutes.TakeLast(5).ToList();
            record.ConsecutiveFullMatches = player.ConsecutiveFullMatches;
            record.SeasonFatigue = player.SeasonFatigue;
            record.ConsecutiveStarts = player.ConsecutiveStarts;
        }
    }
}
