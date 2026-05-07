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
        var outputDataFolder = Path.Combine(AppContext.BaseDirectory, "Data", "Json");
        var activeSquadFileName = GetActiveSquadFileName(outputDataFolder);
        if (string.IsNullOrWhiteSpace(activeSquadFileName))
        {
            return;
        }

        foreach (var dataFolder in GetWritableDataFolders(outputDataFolder).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            SaveSquadFormStatuses(Path.Combine(dataFolder, activeSquadFileName), teams);
        }
    }

    private static string? GetActiveSquadFileName(string dataFolder)
    {
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

            var formText = PlayerFormStatusService.ToDisplayText(player.FormStatus);
            record.FormStatus = formText;
            record.Form = formText;
        }
    }
}
