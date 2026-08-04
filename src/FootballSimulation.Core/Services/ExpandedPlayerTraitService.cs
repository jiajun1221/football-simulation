using System.Globalization;
using System.Text;
using FootballSimulation.Models;

namespace FootballSimulation.Services;

public static class ExpandedPlayerTraitService
{
    private static readonly IReadOnlyDictionary<string, PlayerTrait> CuratedTraits =
        new Dictionary<string, PlayerTrait>(StringComparer.OrdinalIgnoreCase)
        {
            ["erlinghaaland"] = PlayerTrait.Strong,
            ["virgilvandijk"] = PlayerTrait.Strong,
            ["antoniorudiger"] = PlayerTrait.Strong,
            ["nickwoltemade"] = PlayerTrait.TargetForward,
            ["benjaminsesko"] = PlayerTrait.TargetForward,
            ["colepalmer"] = PlayerTrait.FirstTouch,
            ["pedri"] = PlayerTrait.FirstTouch,
            ["moisescaicedo"] = PlayerTrait.BallWinner,
            ["declanrice"] = PlayerTrait.BallWinner,
            ["federicovalverde"] = PlayerTrait.RelentlessPresser,
            ["trentalexanderarnold"] = PlayerTrait.CrossingSpecialist,
            ["reecejames"] = PlayerTrait.CrossingSpecialist,
            ["thibautcourtois"] = PlayerTrait.CrossClaimer,
            ["mikemaignan"] = PlayerTrait.ShotStopper,
            ["gianluigidonnarumma"] = PlayerTrait.ShotStopper
        };

    private static readonly HashSet<PlayerTrait> ExpandedTraits =
    [
        PlayerTrait.Strong, PlayerTrait.Acrobatics, PlayerTrait.FirstTouch,
        PlayerTrait.Poacher, PlayerTrait.TargetForward, PlayerTrait.RelentlessPresser,
        PlayerTrait.BallWinner, PlayerTrait.RecoveryPace, PlayerTrait.Composed,
        PlayerTrait.CrossingSpecialist, PlayerTrait.ShotStopper, PlayerTrait.CrossClaimer
    ];

    public static bool ApplyInferredTrait(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        player.Traits ??= [];
        if (player.OverallRating < 78 || player.Traits.Any(ExpandedTraits.Contains) ||
            player.Traits.Count >= GetMaximumTraitCount(player))
        {
            return false;
        }

        var selected = CuratedTraits.TryGetValue(NormalizeName(player.Name), out var curated) && IsPositionEligible(player, curated)
            ? curated
            : SelectBestInferredTrait(player);
        if (selected is null || player.Traits.Contains(selected.Value))
        {
            return false;
        }

        player.Traits.Add(selected.Value);
        return true;
    }

    public static bool IsExpandedTrait(PlayerTrait trait) => ExpandedTraits.Contains(trait);

    public static int GetMaximumTraitCount(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);
        var baseLimit = GetMaximumTraitCount(player.OverallRating);
        var isHighPotentialProspect = player.Age is <= 23 && player.PotentialOverall is >= 90;
        return isHighPotentialProspect ? Math.Min(5, baseLimit + 1) : baseLimit;
    }

    public static int GetMaximumTraitCount(int overall) => overall switch
    {
        < 78 => 2,
        < 86 => 3,
        < 90 => 4,
        _ => 6
    };

    private static PlayerTrait? SelectBestInferredTrait(Player player)
    {
        return GetCandidates(player)
            .Where(candidate => candidate.Score >= 82)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Priority)
            .Select(candidate => (PlayerTrait?)candidate.Trait)
            .FirstOrDefault();
    }

    private static IEnumerable<TraitCandidate> GetCandidates(Player player)
    {
        var exactPosition = PositionSuitabilityService.NormalizeExactPosition(player.PreferredPosition);
        if (exactPosition == "GK" || player.Position == Position.Goalkeeper)
        {
            yield return new(PlayerTrait.ShotStopper, Math.Max(player.OverallRating, player.Defending), 0);
            yield return new(PlayerTrait.CrossClaimer, (player.Physical + player.OverallRating) / 2.0, 1);
            yield break;
        }

        yield return new(PlayerTrait.FirstTouch, player.Dribbling * 0.60 + player.Passing * 0.40, 0);
        yield return new(PlayerTrait.Strong, player.Physical, 1);
        yield return new(PlayerTrait.Composed, (player.Passing + player.Shooting + player.OverallRating) / 3.0, 2);

        if (exactPosition is "ST" or "CF" || player.Position == Position.Forward)
        {
            yield return new(PlayerTrait.Poacher, player.Finishing * 0.65 + player.Shooting * 0.35, 3);
            yield return new(PlayerTrait.TargetForward, player.Physical * 0.70 + player.OverallRating * 0.30, 4);
            yield return new(PlayerTrait.Acrobatics, (player.Dribbling + player.Shooting) / 2.0, 5);
        }

        if (exactPosition is "LB" or "RB" or "LWB" or "RWB" or "LW" or "RW" or "LM" or "RM")
        {
            yield return new(PlayerTrait.CrossingSpecialist, player.Passing * 0.70 + player.Dribbling * 0.30, 3);
            yield return new(PlayerTrait.RecoveryPace, player.Pace * 0.70 + player.Defending * 0.30, 4);
        }

        if (exactPosition is "CB" or "CDM" or "CM" or "LB" or "RB" ||
            player.Position is Position.Defender or Position.Midfielder)
        {
            yield return new(PlayerTrait.BallWinner, player.Defending * 0.70 + player.Physical * 0.30, 3);
            yield return new(PlayerTrait.RecoveryPace, player.Pace * 0.60 + player.Defending * 0.40, 4);
            yield return new(PlayerTrait.RelentlessPresser,
                player.Physical * 0.45 + player.Defending * 0.35 + player.Pace * 0.20, 5);
        }
    }

    private static bool IsPositionEligible(Player player, PlayerTrait trait)
    {
        var isGoalkeeper = player.Position == Position.Goalkeeper ||
            PositionSuitabilityService.NormalizeExactPosition(player.PreferredPosition) == "GK";
        return trait is PlayerTrait.ShotStopper or PlayerTrait.CrossClaimer ? isGoalkeeper : !isGoalkeeper;
    }

    private static string NormalizeName(string value) =>
        new(value.Normalize(NormalizationForm.FormD)
            .Where(character => char.IsLetterOrDigit(character) &&
                CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Select(char.ToLowerInvariant)
            .ToArray());

    private sealed record TraitCandidate(PlayerTrait Trait, double Score, int Priority);
}
