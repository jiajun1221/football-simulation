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

    public static int UnlockOverallMilestoneTraits(Player player, int previousOverall)
    {
        ArgumentNullException.ThrowIfNull(player);
        player.Traits = EnsureMinimumTraits(
            player.Traits,
            player.Position,
            player.PreferredPosition,
            player.Name);

        if (!IsYoungPlayer(player.Age))
        {
            return 0;
        }

        return UnlockOverallMilestoneTraits(
            player.Traits,
            player.Position,
            player.PreferredPosition,
            previousOverall,
            player.OverallRating);
    }

    public static int UnlockOverallMilestoneTraits(YouthPlayer player, int previousOverall)
    {
        ArgumentNullException.ThrowIfNull(player);
        player.Traits = EnsureMinimumTraits(
            player.Traits,
            player.Position,
            player.PreferredPosition,
            player.Name);

        return UnlockOverallMilestoneTraits(
            player.Traits,
            player.Position,
            player.PreferredPosition,
            previousOverall,
            player.CurrentOVR);
    }

    private static int UnlockOverallMilestoneTraits(
        List<PlayerTrait> traits,
        Position position,
        string preferredPosition,
        int previousOverall,
        int currentOverall)
    {
        var unlockCount = 0;
        if (previousOverall < 85 && currentOverall >= 85)
        {
            unlockCount += AddBestAvailableTrait(traits, position, preferredPosition);
        }

        if (previousOverall < 90 && currentOverall >= 90)
        {
            unlockCount += AddBestAvailableTrait(traits, position, preferredPosition);
        }

        return unlockCount;
    }

    private static int AddBestAvailableTrait(List<PlayerTrait> traits, Position position, string preferredPosition)
    {
        foreach (var trait in GetTraitUnlockPriority(position, preferredPosition))
        {
            if (traits.Contains(trait))
            {
                continue;
            }

            traits.Add(trait);
            return 1;
        }

        return 0;
    }

    private static void AddTraitIfMissing(List<PlayerTrait> traits, PlayerTrait trait)
    {
        if (!traits.Contains(trait))
        {
            traits.Add(trait);
        }
    }

    private static bool IsYoungPlayer(int? age)
    {
        return !age.HasValue || age.Value <= 23;
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

    private static IReadOnlyList<PlayerTrait> GetTraitUnlockPriority(Position position, string preferredPosition)
    {
        return PositionSuitabilityService.NormalizeExactPosition(preferredPosition) switch
        {
            "GK" =>
            [
                PlayerTrait.OneOnOnes,
                PlayerTrait.RushesOutOfGoal,
                PlayerTrait.Puncher,
                PlayerTrait.LongThrower,
                PlayerTrait.BigMatchPlayer,
                PlayerTrait.Leadership
            ],
            "CB" =>
            [
                PlayerTrait.Interceptor,
                PlayerTrait.PowerHeader,
                PlayerTrait.AerialThreat,
                PlayerTrait.DivesIntoTackles,
                PlayerTrait.TeamPlayer,
                PlayerTrait.BigMatchPlayer
            ],
            "LB" or "RB" or "LWB" or "RWB" =>
            [
                PlayerTrait.Engine,
                PlayerTrait.EarlyCrosser,
                PlayerTrait.DivesIntoTackles,
                PlayerTrait.Interceptor,
                PlayerTrait.LongPasser,
                PlayerTrait.TeamPlayer
            ],
            "CDM" =>
            [
                PlayerTrait.TeamPlayer,
                PlayerTrait.Interceptor,
                PlayerTrait.PressResistant,
                PlayerTrait.LongPasser,
                PlayerTrait.BoxToBox,
                PlayerTrait.Leadership
            ],
            "CM" =>
            [
                PlayerTrait.BoxToBox,
                PlayerTrait.Playmaker,
                PlayerTrait.PressResistant,
                PlayerTrait.LongPasser,
                PlayerTrait.Engine,
                PlayerTrait.TeamPlayer
            ],
            "CAM" =>
            [
                PlayerTrait.Playmaker,
                PlayerTrait.Flair,
                PlayerTrait.TechnicalDribbler,
                PlayerTrait.LongShotTaker,
                PlayerTrait.PressResistant,
                PlayerTrait.BigMatchPlayer
            ],
            "LW" or "RW" =>
            [
                PlayerTrait.Rapid,
                PlayerTrait.TechnicalDribbler,
                PlayerTrait.SpeedDribbler,
                PlayerTrait.Flair,
                PlayerTrait.EarlyCrosser,
                PlayerTrait.FinesseShot
            ],
            "ST" or "CF" =>
            [
                PlayerTrait.ClinicalFinisher,
                PlayerTrait.TriesToBeatOffsideTrap,
                PlayerTrait.FinesseShot,
                PlayerTrait.PowerHeader,
                PlayerTrait.AerialThreat,
                PlayerTrait.BigMatchPlayer
            ],
            _ => position switch
            {
                Position.Goalkeeper =>
                [
                    PlayerTrait.OneOnOnes,
                    PlayerTrait.RushesOutOfGoal,
                    PlayerTrait.Puncher,
                    PlayerTrait.LongThrower,
                    PlayerTrait.BigMatchPlayer
                ],
                Position.Defender =>
                [
                    PlayerTrait.Interceptor,
                    PlayerTrait.DivesIntoTackles,
                    PlayerTrait.PowerHeader,
                    PlayerTrait.TeamPlayer,
                    PlayerTrait.BigMatchPlayer
                ],
                Position.Midfielder =>
                [
                    PlayerTrait.TeamPlayer,
                    PlayerTrait.Playmaker,
                    PlayerTrait.BoxToBox,
                    PlayerTrait.PressResistant,
                    PlayerTrait.LongPasser
                ],
                Position.Forward =>
                [
                    PlayerTrait.ClinicalFinisher,
                    PlayerTrait.Rapid,
                    PlayerTrait.FinesseShot,
                    PlayerTrait.TechnicalDribbler,
                    PlayerTrait.BigMatchPlayer
                ],
                _ =>
                [
                    PlayerTrait.TeamPlayer,
                    PlayerTrait.Engine,
                    PlayerTrait.BigMatchPlayer,
                    PlayerTrait.Leadership
                ]
            }
        };
    }

    private static bool IsEstevao(string playerName)
    {
        return playerName.Equals("Estevao", StringComparison.OrdinalIgnoreCase) ||
            playerName.Equals("Est\u00eav\u00e3o", StringComparison.OrdinalIgnoreCase);
    }
}
