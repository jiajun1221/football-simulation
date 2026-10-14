using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Wpf.Services;

public static class PlayerRosterKeyService
{
    public static string CreateKey(Player player)
    {
        if (!string.IsNullOrWhiteSpace(player.PlayerId))
        {
            return $"id:{Normalize(player.PlayerId)}";
        }

        var normalizedName = Normalize(player.Name);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return $"unknown:{player.SquadNumber}:{PositionSuitabilityService.NormalizeExactPosition(player.PreferredPosition)}";
        }

        var nationality = Normalize(
            !string.IsNullOrWhiteSpace(player.NationalityCode)
                ? player.NationalityCode
                : !string.IsNullOrWhiteSpace(player.NationalityName)
                    ? player.NationalityName
                    : player.Nationality);
        var position = PositionSuitabilityService.NormalizeExactPosition(player.PreferredPosition);
        var squadNumber = player.SquadNumber > 0 ? player.SquadNumber.ToString() : "NO";

        return $"fallback:name:{normalizedName}|no:{squadNumber}|nat:{nationality}|pos:{position}";
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Concat(value.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit));
    }
}
