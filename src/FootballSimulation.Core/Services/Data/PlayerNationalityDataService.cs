using System.Text.Json;
using FootballSimulation.Data.JsonModels;
using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class PlayerNationalityDataService
{
    private const string NationalityFileName = "player-nationalities.json";

    private static readonly Lazy<Dictionary<string, PlayerNationalityRecord>> RecordsByName = new(LoadRecords);

    public static bool TryApply(Player player)
    {
        if (!RecordsByName.Value.TryGetValue(NormalizeKey(player.Name), out var record))
        {
            return false;
        }

        player.NationalityCode = record.NationalityCode;
        player.NationalityName = record.NationalityName;
        player.Nationality = record.NationalityName;
        player.FlagImagePath = record.FlagImagePath;
        return true;
    }

    public static bool IsMissingOrDefault(Player player)
    {
        return string.IsNullOrWhiteSpace(player.NationalityCode) ||
            string.IsNullOrWhiteSpace(player.NationalityName) ||
            string.IsNullOrWhiteSpace(player.FlagImagePath) ||
            player.NationalityCode.Equals("UN", StringComparison.OrdinalIgnoreCase) ||
            player.NationalityCode.Equals("GBR", StringComparison.OrdinalIgnoreCase) ||
            player.NationalityName.Equals("United Kingdom", StringComparison.OrdinalIgnoreCase) ||
            player.NationalityName.Equals("Unknown nationality", StringComparison.OrdinalIgnoreCase) ||
            player.FlagImagePath.EndsWith("/united-kingdom.png", StringComparison.OrdinalIgnoreCase) ||
            player.FlagImagePath.EndsWith("\\united-kingdom.png", StringComparison.OrdinalIgnoreCase) ||
            player.FlagImagePath.EndsWith("/default.png", StringComparison.OrdinalIgnoreCase) ||
            player.FlagImagePath.EndsWith("\\default.png", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, PlayerNationalityRecord> LoadRecords()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "Json", NationalityFileName);
        if (!File.Exists(path))
        {
            return new Dictionary<string, PlayerNationalityRecord>(StringComparer.OrdinalIgnoreCase);
        }

        var json = File.ReadAllText(path);
        var data = JsonSerializer.Deserialize<PlayerNationalityDataFile>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return (data?.Players ?? [])
            .Where(record => !string.IsNullOrWhiteSpace(record.Name))
            .GroupBy(record => NormalizeKey(record.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeKey(string value)
    {
        return new string(value
            .Normalize()
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }
}
