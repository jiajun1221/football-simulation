using FootballSimulation.Data.JsonModels;
using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class PlayerStatMappingService
{
    public Player MapToPlayer(PlayerDataRecord record)
    {
        var position = MapPosition(record.Position);
        var overall = ClampStat(record.OverallRating);
        var fatigue = Math.Clamp(record.Fatigue ?? 0, 0, 100);
        var stamina = ClampStat(record.Stamina ?? CalculateStamina(position, overall));
        var loadedStatus = PlayerFormStatus.Average;
        var form = PlayerFormStatusService.ToDisplayText(loadedStatus);
        var currentForm = PlayerFormStatusService.ToCurrentForm(loadedStatus);
        var preferredPosition = GetPreferredPosition(record.Position, record.PreferredPosition);
        var injuryState = CreateInjuryState(
            record.IsInjured,
            record.InjuryType,
            record.InjurySeverity,
            record.InjuryRecoveryMatches,
            record.IsSeasonEndingInjury);

        return new Player
        {
            Name = record.Name,
            SquadNumber = record.SquadNumber,
            Position = position,
            PreferredPosition = preferredPosition,
            SecondaryPositions = MapSecondaryPositions(record.SecondaryPositions),
            AssignedPosition = preferredPosition,
            OverallRating = overall,
            BaseOverallRating = overall,
            Age = record.Age,
            PotentialOverall = record.PotentialOverall,
            Form = form,
            IsStarter = record.IsStarter,
            IsOnPitch = record.IsStarter,
            CurrentForm = currentForm,
            FormStatus = loadedStatus,
            Morale = ClampStat(record.Morale ?? 50),
            Traits = MapTraits(record.Traits),
            Attack = CalculateAttack(position, overall),
            Defense = CalculateDefense(position, overall),
            Passing = CalculatePassing(position, overall),
            Stamina = stamina,
            CurrentStamina = CalculateCurrentStamina(stamina, fatigue),
            Fatigue = fatigue,
            IsInjured = injuryState.IsInjured,
            InjuryType = injuryState.Type,
            InjurySeverity = injuryState.Severity,
            InjuryRecoveryMatches = injuryState.RecoveryMatches,
            IsSeasonEndingInjury = injuryState.IsSeasonEnding,
            IsSuspended = record.IsSuspended ?? false,
            MatchesPlayedRecently = Math.Max(0, record.MatchesPlayedRecently ?? 0),
            Finishing = CalculateFinishing(position, overall)
        };
    }

    public Player MapToPlayer(SquadPlayerRecord record, bool isStarter)
    {
        var position = MapPosition(record.Position);
        var overall = ClampStat(record.OverallRating);
        var fatigue = Math.Clamp(record.Fatigue, 0, 100);
        var stamina = ClampStat(record.Stamina ?? CalculateStamina(position, overall));
        var loadedStatus = PlayerFormStatus.Average;
        var form = PlayerFormStatusService.ToDisplayText(loadedStatus);
        var currentForm = PlayerFormStatusService.ToCurrentForm(loadedStatus);
        var preferredPosition = GetPreferredPosition(record.Position, record.PreferredPosition);
        var injuryState = CreateInjuryState(
            record.IsInjured,
            record.InjuryType,
            record.InjurySeverity,
            record.InjuryRecoveryMatches,
            record.IsSeasonEndingInjury);

        return new Player
        {
            Name = record.Name,
            SquadNumber = record.SquadNumber,
            Position = position,
            PreferredPosition = preferredPosition,
            SecondaryPositions = MapSecondaryPositions(record.SecondaryPositions),
            AssignedPosition = preferredPosition,
            OverallRating = overall,
            BaseOverallRating = overall,
            Age = record.Age,
            PotentialOverall = record.PotentialOverall,
            Form = form,
            IsStarter = isStarter,
            IsOnPitch = isStarter,
            CurrentForm = currentForm,
            FormStatus = loadedStatus,
            Morale = ClampStat(record.Morale ?? 50),
            Traits = MapTraits(record.Traits),
            Attack = CalculateAttack(position, overall),
            Defense = CalculateDefense(position, overall),
            Passing = CalculatePassing(position, overall),
            Stamina = stamina,
            CurrentStamina = CalculateCurrentStamina(stamina, fatigue),
            Fatigue = fatigue,
            IsInjured = injuryState.IsInjured,
            InjuryType = injuryState.Type,
            InjurySeverity = injuryState.Severity,
            InjuryRecoveryMatches = injuryState.RecoveryMatches,
            IsSeasonEndingInjury = injuryState.IsSeasonEnding,
            IsSuspended = record.IsSuspended ?? false,
            MatchesPlayedRecently = Math.Max(0, record.MatchesPlayedRecently ?? 0),
            Finishing = CalculateFinishing(position, overall)
        };
    }

    private static List<PlayerTrait> MapTraits(IEnumerable<string> traits)
    {
        return traits
            .Select(NormalizeTraitName)
            .Where(name => Enum.TryParse<PlayerTrait>(name, ignoreCase: true, out _))
            .Select(name => Enum.Parse<PlayerTrait>(name, ignoreCase: true))
            .Distinct()
            .ToList();
    }

    private static List<string> MapSecondaryPositions(IEnumerable<string> positions)
    {
        return MapExactPositions(positions);
    }

    private static List<string> MapExactPositions(IEnumerable<string> positions)
    {
        return positions
            .Select(PositionSuitabilityService.NormalizeExactPosition)
            .Where(position => position.Length > 0)
            .Distinct()
            .ToList();
    }

    private static string GetPreferredPosition(string sourcePosition, string? preferredPosition)
    {
        var explicitPreferred = PositionSuitabilityService.NormalizeExactPosition(preferredPosition);
        if (explicitPreferred.Length > 0)
        {
            return explicitPreferred;
        }

        var exactSourcePosition = PositionSuitabilityService.NormalizeExactPosition(sourcePosition);
        return exactSourcePosition;
    }

    private static string NormalizeTraitName(string trait)
    {
        return trait
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace("(", string.Empty)
            .Replace(")", string.Empty)
            .Trim();
    }

    private static InjuryState CreateInjuryState(
        bool? isInjured,
        string? injuryType,
        string? injurySeverity,
        int? recoveryMatches,
        bool? isSeasonEnding)
    {
        var severity = Enum.TryParse<InjurySeverity>(injurySeverity, ignoreCase: true, out var parsedSeverity)
            ? parsedSeverity
            : (InjurySeverity?)null;
        var seasonEnding = isSeasonEnding == true || severity == InjurySeverity.SeasonEnding;
        var recovery = Math.Max(0, recoveryMatches ?? 0);
        var unavailable = isInjured == true || seasonEnding || recovery > 0;

        return new InjuryState(
            unavailable,
            injuryType ?? string.Empty,
            severity,
            seasonEnding ? Math.Max(recovery, 99) : recovery,
            seasonEnding);
    }

    private static Position MapPosition(string position)
    {
        return position.Trim().ToLowerInvariant() switch
        {
            "goalkeeper" or "gk" => Position.Goalkeeper,
            "defender" or "defence" or "defense" or "df" or "def" or "lb" or "rb" or "cb" => Position.Defender,
            "midfielder" or "midfield" or "mf" or "mid" or "cm" or "cam" or "cdm" => Position.Midfielder,
            "forward" or "striker" or "winger" or "fw" or "fwd" or "lw" or "rw" or "st" => Position.Forward,
            _ => Position.Midfielder
        };
    }

    private static int CalculateAttack(Position position, int overall)
    {
        return position switch
        {
            Position.Goalkeeper => ClampStat(overall - 55),
            Position.Defender => ClampStat(overall - 32),
            Position.Midfielder => ClampStat(overall - 6),
            Position.Forward => ClampStat(overall + 4),
            _ => overall
        };
    }

    private static int CalculateDefense(Position position, int overall)
    {
        return position switch
        {
            Position.Goalkeeper => ClampStat(overall + 8),
            Position.Defender => ClampStat(overall + 4),
            Position.Midfielder => ClampStat(overall - 8),
            Position.Forward => ClampStat(overall - 38),
            _ => overall
        };
    }

    private static int CalculatePassing(Position position, int overall)
    {
        return position switch
        {
            Position.Goalkeeper => ClampStat(overall - 28),
            Position.Defender => ClampStat(overall - 12),
            Position.Midfielder => ClampStat(overall + 3),
            Position.Forward => ClampStat(overall - 10),
            _ => overall
        };
    }

    private static int CalculateStamina(Position position, int overall)
    {
        return position switch
        {
            Position.Goalkeeper => ClampStat(overall - 8),
            Position.Defender => ClampStat(overall),
            Position.Midfielder => ClampStat(overall + 2),
            Position.Forward => ClampStat(overall),
            _ => overall
        };
    }

    private static int CalculateFinishing(Position position, int overall)
    {
        return position switch
        {
            Position.Goalkeeper => ClampStat(overall - 65),
            Position.Defender => ClampStat(overall - 42),
            Position.Midfielder => ClampStat(overall - 10),
            Position.Forward => ClampStat(overall + 6),
            _ => overall
        };
    }

    private static int ClampStat(int value)
    {
        return Math.Clamp(value, 1, 100);
    }

    private static double CalculateCurrentStamina(int stamina, int fatigue)
    {
        return Math.Clamp(stamina * ((100 - fatigue) / 100.0), 0, stamina);
    }

    private sealed record InjuryState(
        bool IsInjured,
        string Type,
        InjurySeverity? Severity,
        int RecoveryMatches,
        bool IsSeasonEnding);
}
