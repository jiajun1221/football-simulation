using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class PlayerTraitAssignmentService
{
    public static List<PlayerTrait> EnsureMinimumTraits(
        IEnumerable<PlayerTrait>? traits,
        Position position,
        string preferredPosition,
        string playerName = "")
    {
        var assignedTraits = traits?
            .Distinct()
            .ToList() ?? [];

        if (IsEstevao(playerName))
        {
            AddTraitIfMissing(assignedTraits, PlayerTrait.Rapid);
            AddTraitIfMissing(assignedTraits, PlayerTrait.TechnicalDribbler);
            return assignedTraits;
        }

        if (assignedTraits.Count == 0)
        {
            assignedTraits.Add(GetDefaultTrait(position, preferredPosition));
        }

        return assignedTraits;
    }

    public static void EnsureMinimumTraits(Player player)
    {
        player.Traits = EnsureMinimumTraits(
            player.Traits,
            player.Position,
            player.PreferredPosition,
            player.Name);
    }

    private static void AddTraitIfMissing(List<PlayerTrait> traits, PlayerTrait trait)
    {
        if (!traits.Contains(trait))
        {
            traits.Add(trait);
        }
    }

    private static PlayerTrait GetDefaultTrait(Position position, string preferredPosition)
    {
        return PositionSuitabilityService.NormalizeExactPosition(preferredPosition) switch
        {
            "GK" => PlayerTrait.OneOnOnes,
            "CB" => PlayerTrait.Interceptor,
            "LB" or "RB" or "LWB" or "RWB" => PlayerTrait.Engine,
            "CDM" => PlayerTrait.TeamPlayer,
            "CM" => PlayerTrait.BoxToBox,
            "CAM" => PlayerTrait.Playmaker,
            "LW" or "RW" => PlayerTrait.Rapid,
            "ST" or "CF" => PlayerTrait.ClinicalFinisher,
            _ => position switch
            {
                Position.Goalkeeper => PlayerTrait.OneOnOnes,
                Position.Defender => PlayerTrait.Interceptor,
                Position.Midfielder => PlayerTrait.TeamPlayer,
                Position.Forward => PlayerTrait.ClinicalFinisher,
                _ => PlayerTrait.TeamPlayer
            }
        };
    }

    private static bool IsEstevao(string playerName)
    {
        return playerName.Equals("Estevao", StringComparison.OrdinalIgnoreCase) ||
            playerName.Equals("Estêvão", StringComparison.OrdinalIgnoreCase);
    }
}
