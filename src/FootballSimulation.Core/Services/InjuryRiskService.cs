using FootballSimulation.Models;

namespace FootballSimulation.Services;

public sealed class InjuryRiskService
{
    private const int MaximumRecentLoad = 7;

    public InjuryOccurrence? TryCreateMatchInjury(
        Match match,
        int minute,
        EventType? previousEventType,
        Random random)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(random);

        if (ShouldSuppressMatchInjury(match, minute))
        {
            return null;
        }

        var candidates = GetMatchInjuryCandidates(match.HomeTeam, match.AwayTeam)
            .Select(player => new InjuryCandidate(
                player.Team,
                player.Player,
                CalculatePlayerMatchInjuryWeight(player.Player, player.Team, GetOpponent(match, player.Team), minute, previousEventType)))
            .Where(candidate => candidate.Weight > 0)
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var minuteChance = CalculateMatchMinuteInjuryChance(match, minute, previousEventType);
        if (random.NextDouble() >= minuteChance)
        {
            return null;
        }

        var selected = ChooseWeighted(candidates, random);
        var injuryCause = ChooseMatchInjuryCause(selected.Player, selected.Team, GetOpponent(match, selected.Team), previousEventType, random);
        ApplyInjury(selected.Player, injuryCause, random);
        return new InjuryOccurrence(selected.Team, selected.Player, injuryCause);
    }

    public InjuryOccurrence? TryCreatePreparationInjury(Team team, int roundNumber, Random random)
    {
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(random);

        var candidates = team.Players
            .Concat(team.Substitutes)
            .Where(IsAvailableForTrainingRisk)
            .Select(player => new InjuryCandidate(team, player, CalculatePreparationInjuryWeight(player)))
            .Where(candidate => candidate.Weight > 0)
            .ToList();
        if (candidates.Count == 0 || candidates.Count(player => !player.Player.IsInjured) < 14)
        {
            return null;
        }

        var chance = CalculatePreparationInjuryChance(team, roundNumber);
        if (random.NextDouble() >= chance)
        {
            return null;
        }

        var selected = ChooseWeighted(candidates, random);
        var injuryCause = ChoosePreparationInjuryCause(selected.Player, random);
        ApplyInjury(selected.Player, injuryCause, random);
        return new InjuryOccurrence(team, selected.Player, injuryCause);
    }

    public void ApplyPostMatchLoad(Match match)
    {
        ArgumentNullException.ThrowIfNull(match);

        ApplyPostMatchLoad(match, match.HomeTeam);
        ApplyPostMatchLoad(match, match.AwayTeam);
    }

    public double CalculateMatchMinuteInjuryChance(Match match, int minute, EventType? previousEventType)
    {
        ArgumentNullException.ThrowIfNull(match);

        var activePlayers = GetMatchInjuryCandidates(match.HomeTeam, match.AwayTeam)
            .Select(candidate => candidate.Player)
            .ToList();
        if (activePlayers.Count == 0)
        {
            return 0;
        }

        var averageRisk = activePlayers.Average(player => CalculatePlayerConditionRisk(player, minute));
        var intensityRisk = CalculateMatchIntensityRisk(match.HomeTeam) + CalculateMatchIntensityRisk(match.AwayTeam);
        var physicalEventRisk = GetPhysicalEventRisk(previousEventType);
        var lateGameRisk = minute switch
        {
            >= 82 => 0.0014,
            >= 70 => 0.0008,
            >= 55 => 0.00035,
            _ => 0.0
        };

        return Math.Clamp(
            0.00055 + averageRisk / 95000.0 + intensityRisk + physicalEventRisk + lateGameRisk,
            0.00025,
            0.0075);
    }

    public double CalculatePlayerMatchInjuryWeight(
        Player player,
        Team team,
        Team opponent,
        int minute,
        EventType? previousEventType)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(team);
        ArgumentNullException.ThrowIfNull(opponent);

        var attributes = PlayerAttributeService.GetAttributes(player);
        var staminaRisk = player.Stamina switch
        {
            <= 12 => 42.0,
            <= 25 => 28.0,
            <= 40 => 16.0,
            <= 58 => 7.0,
            _ => 0.0
        };
        var minutesRisk = minute switch
        {
            >= 82 => 9.0,
            >= 70 => 5.0,
            >= 55 => 2.0,
            _ => 0.0
        };
        var loadRisk = Math.Min(MaximumRecentLoad, player.MatchesPlayedRecently) * 5.0;
        var traitRisk = player.Traits.Contains(PlayerTrait.InjuryProne) ? 24.0 : 0.0;
        var duelRisk =
            (player.Traits.Contains(PlayerTrait.DivesIntoTackles) ? 5.0 : 0.0) +
            (player.Traits.Contains(PlayerTrait.Rapid) || player.Traits.Contains(PlayerTrait.SpeedDribbler) ? 4.0 : 0.0) +
            (player.Traits.Contains(PlayerTrait.PowerHeader) || player.Traits.Contains(PlayerTrait.AerialThreat) ? 3.5 : 0.0);
        var opponentAggressionRisk = GetOpponentAggressionRisk(opponent);
        var physicalEventRisk = IsPhysicalEvent(previousEventType) ? 10.0 : 0.0;
        var fitnessProtection =
            Math.Max(0, attributes.Physical - 68) * 0.40 +
            (player.Traits.Contains(PlayerTrait.Engine) ? 9.0 : 0.0) +
            Math.Max(0, player.Stamina - 76) * 0.12;

        return Math.Max(1.0, 8.0 + staminaRisk + minutesRisk + loadRisk + traitRisk + duelRisk + opponentAggressionRisk + physicalEventRisk - fitnessProtection);
    }

    public double CalculatePreparationInjuryChance(Team team, int roundNumber)
    {
        ArgumentNullException.ThrowIfNull(team);

        var availablePlayers = team.Players
            .Concat(team.Substitutes)
            .Where(IsAvailableForTrainingRisk)
            .ToList();
        if (availablePlayers.Count < 14)
        {
            return 0;
        }

        var averageRecentLoad = availablePlayers.Average(player => Math.Min(MaximumRecentLoad, player.MatchesPlayedRecently));
        var highLoadPlayers = availablePlayers.Count(player => player.MatchesPlayedRecently >= 4);
        var tacticalIntensity =
            Math.Max(0, team.Tactics.Tempo - 65) / 9000.0 +
            Math.Max(0, team.Tactics.PressingIntensity - 65) / 7000.0;
        var congestedSeasonRisk = roundNumber >= 24 ? 0.002 : 0.0;
        var loadRisk = averageRecentLoad / 1200.0 + highLoadPlayers / 5000.0;

        return Math.Clamp(0.0025 + tacticalIntensity + loadRisk + congestedSeasonRisk, 0.001, 0.026);
    }

    public static void ApplyInjury(Player player, string injuryCause, Random random)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(random);

        var severity = ChooseInjurySeverity(player, injuryCause, random);
        player.IsInjured = true;
        player.NewlyInjuredThisMatch = true;
        player.InjurySeverity = severity;
        player.InjuryType = ChooseInjuryType(injuryCause, severity, random);
        player.IsSeasonEndingInjury = severity == InjurySeverity.SeasonEnding;
        player.InjuryRecoveryMatches = severity switch
        {
            InjurySeverity.Minor => random.Next(1, 4),
            InjurySeverity.Moderate => random.Next(4, 11),
            InjurySeverity.Serious => random.Next(15, 31),
            InjurySeverity.SeasonEnding => 99,
            _ => 1
        };
        player.Stamina = 0;
        player.LiveMatchModifier = 0.25;
    }

    private static void ApplyPostMatchLoad(Match match, Team team)
    {
        foreach (var player in team.Players.Concat(team.Substitutes).Distinct())
        {
            var performance = match.PlayerPerformances.FirstOrDefault(existing =>
                string.Equals(existing.TeamName, team.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existing.PlayerName, player.Name, StringComparison.OrdinalIgnoreCase));
            var minutesPlayed = performance is null ? 0 : EstimateMinutesPlayed(performance);

            if (player.IsInjured)
            {
                player.MatchesPlayedRecently = Math.Max(0, player.MatchesPlayedRecently - 1);
                continue;
            }

            player.MatchesPlayedRecently = minutesPlayed switch
            {
                >= 80 => Math.Min(MaximumRecentLoad, player.MatchesPlayedRecently + (performance?.FatigueAtEnd >= 65 ? 2 : 1)),
                >= 55 => Math.Min(MaximumRecentLoad, player.MatchesPlayedRecently + 1),
                >= 25 => player.MatchesPlayedRecently,
                _ => Math.Max(0, player.MatchesPlayedRecently - 1)
            };
        }
    }

    private static int EstimateMinutesPlayed(PlayerMatchPerformance performance)
    {
        if (performance.WasSubbedOn && performance.SubstitutionMinute is int subOnMinute)
        {
            return Math.Clamp(90 - subOnMinute + 1, 1, 90);
        }

        if (performance.WasSubbedOff && performance.SubstitutionMinute is int subOffMinute)
        {
            return Math.Clamp(subOffMinute, 1, 90);
        }

        return performance.WasSubstitute ? 0 : 90;
    }

    private static bool ShouldSuppressMatchInjury(Match match, int minute)
    {
        var injuries = match.Events
            .Where(matchEvent => matchEvent.EventType == EventType.Injury)
            .ToList();
        return injuries.Count >= 3 ||
            injuries.Any(matchEvent => matchEvent.Minute == minute) ||
            injuries.Select(matchEvent => matchEvent.Minute).DefaultIfEmpty(-100).Max() + 8 > minute;
    }

    private static IEnumerable<(Team Team, Player Player)> GetMatchInjuryCandidates(Team homeTeam, Team awayTeam)
    {
        return homeTeam.Players
            .Where(IsActiveMatchCandidate)
            .Select(player => (homeTeam, player))
            .Concat(awayTeam.Players.Where(IsActiveMatchCandidate).Select(player => (awayTeam, player)));
    }

    private static bool IsActiveMatchCandidate(Player player)
    {
        return player.IsOnPitch && !player.IsSentOff && !player.IsSuspended && !player.IsInjured;
    }

    private static bool IsAvailableForTrainingRisk(Player player)
    {
        return !player.IsInjured && !player.IsSuspended && !player.IsSentOff;
    }

    private static double CalculatePlayerConditionRisk(Player player, int minute)
    {
        return Math.Max(0, 62 - player.Stamina) * 0.85 +
            Math.Min(MaximumRecentLoad, player.MatchesPlayedRecently) * 4.5 +
            (player.Traits.Contains(PlayerTrait.InjuryProne) ? 18 : 0) +
            (minute >= 75 ? 6 : 0);
    }

    private static double CalculateMatchIntensityRisk(Team team)
    {
        return Math.Max(0, team.Tactics.Tempo - 68) / 18000.0 +
            Math.Max(0, team.Tactics.PressingIntensity - 68) / 12000.0 +
            team.Players.Count(player => player.IsOnPitch && player.Traits.Contains(PlayerTrait.DivesIntoTackles)) * 0.00016;
    }

    private static double CalculatePreparationInjuryWeight(Player player)
    {
        var attributes = PlayerAttributeService.GetAttributes(player);
        var roleRisk = player.Role is PlayerRole.KeyPlayer or PlayerRole.Starter ? 5.0 : 0.0;
        var loadRisk = Math.Min(MaximumRecentLoad, player.MatchesPlayedRecently) * 7.0;
        var traitRisk = player.Traits.Contains(PlayerTrait.InjuryProne) ? 26.0 : 0.0;
        var ageRisk = player.Age >= 31 ? 6.0 : 0.0;
        var protection = Math.Max(0, attributes.Physical - 70) * 0.35 + (player.Traits.Contains(PlayerTrait.Engine) ? 8.0 : 0.0);

        return Math.Max(1.0, 8.0 + roleRisk + loadRisk + traitRisk + ageRisk - protection);
    }

    private static double GetOpponentAggressionRisk(Team opponent)
    {
        return Math.Max(0, opponent.Tactics.PressingIntensity - 70) * 0.13 +
            opponent.Players.Count(player => player.IsOnPitch && player.Traits.Contains(PlayerTrait.DivesIntoTackles)) * 2.8;
    }

    private static double GetPhysicalEventRisk(EventType? eventType)
    {
        return eventType switch
        {
            EventType.Foul => 0.0026,
            EventType.Tackle => 0.0022,
            EventType.Pressure => 0.0014,
            EventType.BlockedPass => 0.0011,
            EventType.DefensiveStop => 0.0009,
            EventType.CornerKick or EventType.SetPieceDanger => 0.0007,
            _ => 0.0
        };
    }

    private static bool IsPhysicalEvent(EventType? eventType)
    {
        return eventType is EventType.Foul
            or EventType.Tackle
            or EventType.Pressure
            or EventType.BlockedPass
            or EventType.DefensiveStop
            or EventType.CornerKick
            or EventType.SetPieceDanger;
    }

    private static Team GetOpponent(Match match, Team team)
    {
        return ReferenceEquals(team, match.HomeTeam) ? match.AwayTeam : match.HomeTeam;
    }

    private static InjuryCandidate ChooseWeighted(IReadOnlyList<InjuryCandidate> candidates, Random random)
    {
        var totalWeight = candidates.Sum(candidate => candidate.Weight);
        var target = random.NextDouble() * totalWeight;
        var runningWeight = 0.0;

        foreach (var candidate in candidates)
        {
            runningWeight += candidate.Weight;
            if (runningWeight >= target)
            {
                return candidate;
            }
        }

        return candidates[^1];
    }

    private static string ChooseMatchInjuryCause(Player player, Team team, Team opponent, EventType? previousEventType, Random random)
    {
        if (previousEventType is EventType.Foul or EventType.Tackle &&
            (GetOpponentAggressionRisk(opponent) > 5 || random.NextDouble() < 0.45))
        {
            return "dangerous tackle";
        }

        if (player.Position == Position.Goalkeeper)
        {
            return "goalkeeper collision";
        }

        if (player.Stamina <= 22)
        {
            return random.NextDouble() < 0.58 ? "over exhaustion" : "sprint muscle pull";
        }

        if (player.Traits.Contains(PlayerTrait.PowerHeader) || player.Traits.Contains(PlayerTrait.AerialThreat))
        {
            return random.NextDouble() < 0.45 ? "aerial duel impact" : "awkward landing";
        }

        if (team.Tactics.Tempo >= 78 && random.NextDouble() < 0.34)
        {
            return "sprint muscle pull";
        }

        return random.NextDouble() switch
        {
            < 0.30 => "heavy collision",
            < 0.54 => "awkward landing",
            < 0.76 => "sprint muscle pull",
            _ => "aerial duel impact"
        };
    }

    private static string ChoosePreparationInjuryCause(Player player, Random random)
    {
        if (player.MatchesPlayedRecently >= 5 && random.NextDouble() < 0.58)
        {
            return "training overload";
        }

        return random.NextDouble() switch
        {
            < 0.36 => "training muscle strain",
            < 0.64 => "training knock",
            < 0.82 => "over exhaustion",
            _ => "awkward landing"
        };
    }

    private static InjurySeverity ChooseInjurySeverity(Player player, string injuryCause, Random random)
    {
        var seriousBonus =
            (injuryCause is "dangerous tackle" or "goalkeeper collision" or "aerial duel impact" ? 0.06 : 0.0) +
            (injuryCause is "training overload" or "over exhaustion" ? 0.025 : 0.0) +
            (player.Traits.Contains(PlayerTrait.InjuryProne) ? 0.04 : 0.0) +
            Math.Min(MaximumRecentLoad, player.MatchesPlayedRecently) * 0.004;
        var roll = random.NextDouble();

        if (roll < 0.004 + seriousBonus / 10.0)
        {
            return InjurySeverity.SeasonEnding;
        }

        if (roll < 0.08 + seriousBonus)
        {
            return InjurySeverity.Serious;
        }

        if (roll < 0.34 + seriousBonus)
        {
            return InjurySeverity.Moderate;
        }

        return InjurySeverity.Minor;
    }

    private static string ChooseInjuryType(string injuryCause, InjurySeverity severity, Random random)
    {
        if (severity == InjurySeverity.SeasonEnding)
        {
            return random.NextDouble() < 0.5 ? "ACL Injury" : "Fracture";
        }

        return injuryCause switch
        {
            "dangerous tackle" => random.NextDouble() < 0.55 ? "Ankle Injury" : "Knee Injury",
            "goalkeeper collision" => random.NextDouble() < 0.5 ? "Shoulder Injury" : "Head Injury",
            "aerial duel impact" => random.NextDouble() < 0.5 ? "Head Injury" : "Back Injury",
            "sprint muscle pull" => random.NextDouble() < 0.65 ? "Hamstring Injury" : "Calf Strain",
            "over exhaustion" => random.NextDouble() < 0.5 ? "Muscle Fatigue" : "Groin Strain",
            "awkward landing" => random.NextDouble() < 0.5 ? "Ankle Injury" : "Knee Injury",
            "training overload" => random.NextDouble() < 0.55 ? "Muscle Fatigue" : "Hamstring Injury",
            "training muscle strain" => random.NextDouble() < 0.55 ? "Calf Strain" : "Groin Strain",
            "training knock" => random.NextDouble() < 0.5 ? "Impact Injury" : "Knock",
            _ => random.NextDouble() < 0.5 ? "Impact Injury" : "Knock"
        };
    }

    private sealed record InjuryCandidate(Team Team, Player Player, double Weight);
}

public sealed record InjuryOccurrence(Team Team, Player Player, string Cause);
