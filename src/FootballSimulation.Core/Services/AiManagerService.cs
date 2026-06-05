using FootballSimulation.Engine;
using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class AiManagerService
{
    private readonly SquadSelectionService _squadSelectionService;
    private readonly FatigueService _fatigueService;
    private readonly InMatchFormationService _formationService = new();

    public AiManagerService()
        : this(new SquadSelectionService(), new FatigueService())
    {
    }

    public AiManagerService(SquadSelectionService squadSelectionService, FatigueService fatigueService)
    {
        _squadSelectionService = squadSelectionService;
        _fatigueService = fatigueService;
    }

    public AiManagerDecision? TryMakeSubstitution(
        Match match,
        Team team,
        int minute,
        MatchSimulationOptions options,
        Random random)
    {
        if (!options.EnableAiSubstitutions || IsHumanControlled(team, options))
        {
            return null;
        }

        if (!ShouldEvaluateSubstitutions(minute))
        {
            return null;
        }

        AdjustTactics(team, match, minute);

        if (_squadSelectionService.CountTeamSubstitutions(match, team.Name) >= MatchConstants.MaxSubstitutionsPerTeam)
        {
            return null;
        }

        var candidate = ChoosePlayerToRemove(match, team, minute, random);
        if (candidate is null)
        {
            return null;
        }

        var replacement = ChooseReplacement(match, team, candidate, random);
        if (replacement is null)
        {
            return null;
        }

        var swapResult = _squadSelectionService.SwapStarterWithSubstitute(team, candidate, replacement, match, minute);
        if (!swapResult.Success)
        {
            return null;
        }

        var replacementPerformance = GetPerformance(match, team, replacement);
        if (replacementPerformance is not null)
        {
            replacementPerformance.Rating += 0.15;
        }

        return new AiManagerDecision(team, candidate, replacement, minute);
    }

    private Player? ChoosePlayerToRemove(Match match, Team team, int minute, Random random)
    {
        var candidates = team.Players
            .Where(player => player.IsOnPitch && !player.IsSentOff)
            .Where(player => player.Position != Position.Goalkeeper || player.IsInjured || _fatigueService.GetFatiguePercentage(player) >= 85)
            .Select(player => new
            {
                Player = player,
                Score = GetRemovalPriority(match, team, player, minute)
            })
            .Where(candidate => candidate.Score >= 35)
            .OrderByDescending(candidate => candidate.Score)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var bestCandidate = candidates[0];
        var chance = Math.Clamp(bestCandidate.Score / 100.0, 0.35, 0.96);

        return random.NextDouble() <= chance ? bestCandidate.Player : null;
    }

    private double GetRemovalPriority(Match match, Team team, Player player, int minute)
    {
        var fatigue = _fatigueService.GetFatiguePercentage(player);
        var performance = GetPerformance(match, team, player);
        var rating = performance?.Rating ?? 6.0;
        var score = 0.0;

        if (player.IsInjured)
        {
            score += 120;
        }

        if (fatigue > 85)
        {
            score += 75;
        }
        else if (fatigue > 70)
        {
            score += 50;
        }
        else if (fatigue > 60 && minute >= 70)
        {
            score += 25;
        }

        if (rating < 5.5)
        {
            score += 60;
        }
        else if (rating < 6.0)
        {
            score += 35;
        }

        var teamIsLosing = IsLosing(match, team);
        var teamIsWinning = IsWinning(match, team);

        if (teamIsLosing && player.Position == Position.Defender && minute >= 60)
        {
            score += 20;
        }

        if (teamIsWinning && player.Position == Position.Forward && minute >= 70)
        {
            score += 18;
        }

        if (minute >= 70)
        {
            score += 12;
        }

        return score;
    }

    private Player? ChooseReplacement(Match match, Team team, Player outgoingPlayer, Random random)
    {
        var substitutes = team.Substitutes
            .Where(player => !player.IsSuspended && !player.IsInjured && !player.IsSentOff)
            .Where(player => !_squadSelectionService.WasPlayerSubstitutedOff(match, team.Name, player.Name))
            .ToList();

        if (substitutes.Count == 0)
        {
            return null;
        }

        var isLosing = IsLosing(match, team);
        var isWinning = IsWinning(match, team);
        var outgoingSlot = PositionSuitabilityService.NormalizeExactPosition(outgoingPlayer.AssignedPosition);
        if (string.IsNullOrWhiteSpace(outgoingSlot))
        {
            outgoingSlot = PositionSuitabilityService.GetDefaultExactPosition(outgoingPlayer.Position);
        }
        var reasonableSubstitutes = substitutes
            .Where(substitute => PositionCompatibilityService.GetCompatibilityScore(substitute, outgoingSlot) > PositionCompatibilityService.Emergency)
            .ToList();

        var candidatePool = reasonableSubstitutes.Count > 0
            ? reasonableSubstitutes
            : substitutes.Where(substitute => PositionCompatibilityService.IsReasonableFit(substitute, outgoingSlot)).ToList();
        if (candidatePool.Count == 0)
        {
            return null;
        }

        return candidatePool
            .OrderByDescending(substitute => GetReplacementScore(substitute, outgoingPlayer, outgoingSlot, isLosing, isWinning))
            .ThenBy(_ => random.NextDouble())
            .FirstOrDefault();
    }

    private double GetReplacementScore(Player substitute, Player outgoingPlayer, string outgoingSlot, bool isLosing, bool isWinning)
    {
        var score = substitute.OverallRating;
        score += PositionCompatibilityService.GetCompatibilityScore(substitute, outgoingSlot) * 3;
        score += 100 - _fatigueService.GetFatiguePercentage(substitute);

        if (substitute.Position == outgoingPlayer.Position)
        {
            score += 30;
        }

        if (isLosing && substitute.Position is Position.Forward or Position.Midfielder)
        {
            score += 28;
        }

        if (isWinning && substitute.Position is Position.Defender or Position.Midfielder)
        {
            score += 24;
        }

        if (substitute.Position == Position.Goalkeeper && outgoingPlayer.Position != Position.Goalkeeper)
        {
            score -= 80;
        }

        return score;
    }

    private static bool ShouldEvaluateSubstitutions(int minute)
    {
        return minute == MatchConstants.HalftimeMinute ||
            minute is >= 60 and <= 85 && minute % 5 == 0;
    }

    private void AdjustTactics(Team team, Match match, int minute)
    {
        if (IsLosing(match, team) && minute >= MatchConstants.HalftimeMinute)
        {
            var presetKey = minute >= 78 ? "gegenpress" : "counter-attack";
            var preset = TacticalProfileService.GetPresets().FirstOrDefault(existing => existing.Key == presetKey);
            if (preset is not null)
            {
                TacticalProfileService.CopyTo(preset.Tactics, team.Tactics);
                if (minute >= 78)
                {
                    team.Tactics.Mentality = Mentality.AllOutAttack;
                    team.Tactics.Tempo = Math.Max(team.Tactics.Tempo, 90);
                    team.Tactics.PressingIntensity = Math.Max(team.Tactics.PressingIntensity, 90);
                }
            }
            else
            {
                team.Tactics.Tempo = Math.Clamp(team.Tactics.Tempo + 10, 1, 100);
                team.Tactics.PressingIntensity = Math.Clamp(team.Tactics.PressingIntensity + 10, 1, 100);
                team.Tactics.Mentality = minute >= 78 ? Mentality.AllOutAttack : Mentality.Attacking;
            }

            if (minute >= 78)
            {
                ApplyBestFormation(team, (team.Name.GetHashCode() & int.MaxValue) % 2 == 0
                    ? ["4-2-4", "3-4-3"]
                    : ["3-4-3", "4-2-4"]);
            }
            else if (minute == MatchConstants.HalftimeMinute &&
                !string.Equals(FormationCatalogService.NormalizeFormationName(team.Formation), "4-3-3 Attack", StringComparison.OrdinalIgnoreCase))
            {
                ApplyBestFormation(team, MapAttackingFormations(team.Formation));
            }
        }

        if (IsWinning(match, team) && minute >= 70)
        {
            var preset = TacticalProfileService.GetPresets().FirstOrDefault(existing => existing.Key == "park-the-bus");
            if (preset is not null)
            {
                TacticalProfileService.CopyTo(preset.Tactics, team.Tactics);
            }
            else
            {
                team.Tactics.Tempo = Math.Clamp(team.Tactics.Tempo - 8, 1, 100);
                team.Tactics.PressingIntensity = Math.Clamp(team.Tactics.PressingIntensity - 8, 1, 100);
                team.Tactics.Mentality = Mentality.Defensive;
            }

            ApplyBestFormation(team, minute >= 80
                ? ["5-4-1", "5-3-2", "4-5-1"]
                : MapDefensiveFormations(team.Formation));
        }

        var opponent = team == match.HomeTeam ? match.AwayTeam : match.HomeTeam;
        if (opponent.Players.Any(player => player.Traits.Contains(PlayerTrait.Rapid)) && team.Tactics.DefensiveLine >= 70)
        {
            team.Tactics.DefensiveLine = Math.Max(45, team.Tactics.DefensiveLine - 14);
        }

        if (team == match.AwayTeam &&
            minute >= 60 &&
            !IsWinning(match, team) &&
            team.Tactics.DefensiveLine >= 55 &&
            match.HomeStats.TotalShots > match.AwayStats.TotalShots + 3)
        {
            team.Tactics.DefensiveLine = Math.Max(35, team.Tactics.DefensiveLine - 15);
            ApplyBestFormation(team, ["4-5-1", "5-4-1"]);
        }
    }

    private void ApplyBestFormation(Team team, IEnumerable<string> candidates)
    {
        var formation = _formationService.ChooseBestFormation(team, candidates);
        if (!string.IsNullOrWhiteSpace(formation))
        {
            _ = _formationService.ApplyFormation(team, formation);
        }
    }

    private static IReadOnlyList<string> MapAttackingFormations(string currentFormation)
    {
        return FormationCatalogService.NormalizeFormationName(currentFormation) switch
        {
            "4-2-3-1 Wide" or "4-2-3-1 Narrow" => ["4-3-3 Attack", "4-2-4"],
            "4-4-2" => ["4-2-4", "4-3-3 Attack"],
            "5-4-1" => ["3-4-3", "4-2-4"],
            _ => ["4-3-3 Attack", "4-2-4", "3-4-3"]
        };
    }

    private static IReadOnlyList<string> MapDefensiveFormations(string currentFormation)
    {
        return FormationCatalogService.NormalizeFormationName(currentFormation) switch
        {
            "4-3-3 Attack" or "4-3-3 Holding" or "4-3-3 Flat" => ["4-5-1", "5-4-1"],
            "4-2-3-1 Wide" or "4-2-3-1 Narrow" => ["5-4-1", "4-5-1"],
            "3-4-3" => ["5-3-2", "5-4-1"],
            _ => ["5-4-1", "5-3-2", "4-5-1"]
        };
    }

    private static bool IsHumanControlled(Team team, MatchSimulationOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.HumanControlledTeamName) &&
            string.Equals(team.Name, options.HumanControlledTeamName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLosing(Match match, Team team)
    {
        return team == match.HomeTeam
            ? match.HomeScore < match.AwayScore
            : match.AwayScore < match.HomeScore;
    }

    private static bool IsWinning(Match match, Team team)
    {
        return team == match.HomeTeam
            ? match.HomeScore > match.AwayScore
            : match.AwayScore > match.HomeScore;
    }

    private static PlayerMatchPerformance? GetPerformance(Match match, Team team, Player player)
    {
        return match.PlayerPerformances.FirstOrDefault(performance =>
            performance.TeamName == team.Name &&
            performance.PlayerName == player.Name);
    }
}

public sealed record AiManagerDecision(Team Team, Player PlayerOff, Player PlayerOn, int Minute);
