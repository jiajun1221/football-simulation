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
        var stamina = CalculateStamina(position, overall);
        var form = NormalizeForm(record.Form);

        return new Player
        {
            Name = record.Name,
            SquadNumber = record.SquadNumber,
            Position = position,
            OverallRating = overall,
            Form = form,
            IsStarter = record.IsStarter,
            CurrentForm = ClampStat(record.CurrentForm ?? MapFormToCurrentForm(form)),
            Morale = ClampStat(record.Morale ?? 50),
            Traits = MapTraits(record.Traits),
            Attack = CalculateAttack(position, overall),
            Defense = CalculateDefense(position, overall),
            Passing = CalculatePassing(position, overall),
            Stamina = stamina,
            CurrentStamina = CalculateCurrentStamina(stamina, fatigue),
            Fatigue = fatigue,
            IsInjured = record.IsInjured ?? false,
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
        var stamina = CalculateStamina(position, overall);
        var form = NormalizeForm(record.Form);

        return new Player
        {
            Name = record.Name,
            SquadNumber = record.SquadNumber,
            Position = position,
            OverallRating = overall,
            Form = form,
            IsStarter = isStarter,
            CurrentForm = MapFormToCurrentForm(form),
            Morale = ClampStat(record.Morale ?? 50),
            Traits = MapTraits(record.Traits),
            Attack = CalculateAttack(position, overall),
            Defense = CalculateDefense(position, overall),
            Passing = CalculatePassing(position, overall),
            Stamina = stamina,
            CurrentStamina = CalculateCurrentStamina(stamina, fatigue),
            Fatigue = fatigue,
            IsInjured = record.IsInjured ?? false,
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

    private static string NormalizeTraitName(string trait)
    {
        return trait.Replace(" ", string.Empty).Replace("-", string.Empty).Trim();
    }

    private static Position MapPosition(string position)
    {
        return position.Trim().ToLowerInvariant() switch
        {
            "goalkeeper" or "gk" => Position.Goalkeeper,
            "defender" or "defence" or "defense" or "df" or "def" => Position.Defender,
            "midfielder" or "midfield" or "mf" or "mid" => Position.Midfielder,
            "forward" or "striker" or "winger" or "fw" or "fwd" => Position.Forward,
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

    private static string NormalizeForm(string? form)
    {
        return form?.Trim().ToLowerInvariant() switch
        {
            "hot" => "Hot",
            "good" => "Good",
            "poor" => "Poor",
            _ => "Average"
        };
    }

    private static int MapFormToCurrentForm(string form)
    {
        return form switch
        {
            "Hot" => 85,
            "Good" => 70,
            "Poor" => 30,
            _ => 50
        };
    }

    private static double CalculateCurrentStamina(int stamina, int fatigue)
    {
        return Math.Clamp(stamina * ((100 - fatigue) / 100.0), 0, stamina);
    }
}
