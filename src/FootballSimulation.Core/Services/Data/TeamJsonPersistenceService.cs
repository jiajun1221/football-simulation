using System.Text.Json;
using System.Text.Json.Serialization;
using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class TeamJsonPersistenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public void SaveTeams(string filePath, Team homeTeam, Team awayTeam)
    {
        var teamFileData = new TeamFileData
        {
            HomeTeam = homeTeam,
            AwayTeam = awayTeam
        };

        var directoryPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        var json = JsonSerializer.Serialize(teamFileData, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    public TeamFileData LoadTeams(string filePath)
    {
        var json = File.ReadAllText(filePath);
        var teamFileData = JsonSerializer.Deserialize<TeamFileData>(json, JsonOptions);

        return teamFileData ?? new TeamFileData();
    }
}
