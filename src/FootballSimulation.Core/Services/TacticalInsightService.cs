using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class TacticalInsightService
{
    public TacticalInsight GenerateInsight(Team selectedTeam, Team opponent)
    {
        var insight = new TacticalInsight();
        AddOpponentThreats(insight, opponent);
        AddLikelyTactics(insight, opponent);
        AddWarnings(insight, selectedTeam, opponent);
        AddRecommendations(insight, selectedTeam, opponent);

        return insight;
    }

    private static void AddOpponentThreats(TacticalInsight insight, Team opponent)
    {
        foreach (var player in opponent.Players.OrderByDescending(GetThreatRating).Take(3))
        {
            var traitText = player.Traits.Count > 0
                ? $" ({string.Join(", ", player.Traits.Take(2))})"
                : string.Empty;

            insight.OpponentThreats.Add($"{player.Name} - {player.Position}, OVR {GetOverall(player)}{traitText}");
        }
    }

    private static void AddLikelyTactics(TacticalInsight insight, Team opponent)
    {
        insight.LikelyTactics.Add($"{opponent.Name} are expected to start in a {opponent.Formation}.");

        if (opponent.Tactics.Mentality == Mentality.Attacking || opponent.Tactics.Tempo >= 65)
        {
            insight.LikelyTactics.Add("Expect fast attacks and early forward passes.");
        }

        if (opponent.Tactics.PressingIntensity >= 65)
        {
            insight.LikelyTactics.Add("They are likely to press aggressively after losing the ball.");
        }

        if (opponent.Tactics.DefensiveLine >= 65)
        {
            insight.LikelyTactics.Add("Their defensive line may be high, leaving space behind.");
        }
    }

    private static void AddWarnings(TacticalInsight insight, Team selectedTeam, Team opponent)
    {
        var tiredStarters = selectedTeam.Players.Where(player => player.Fatigue >= 60 || player.CurrentStamina < player.Stamina * 0.55).ToList();
        if (tiredStarters.Count > 0)
        {
            insight.Warnings.Add($"{tiredStarters.Count} starter(s) look tired. High pressing may create second-half problems.");
        }

        var unavailableStarters = selectedTeam.Players.Where(player => player.IsInjured || player.IsSuspended).ToList();
        if (unavailableStarters.Count > 0)
        {
            insight.Warnings.Add("One or more selected starters are injured or suspended and will perform poorly.");
        }

        if (opponent.Players.Any(player => player.Traits.Contains(PlayerTrait.ClinicalFinisher)) && selectedTeam.Tactics.DefensiveLine >= 65)
        {
            insight.Warnings.Add("A high defensive line is risky against clinical finishers.");
        }

        if (opponent.Players.Any(player => player.Traits.Contains(PlayerTrait.AggressiveTackler)) || opponent.Tactics.PressingIntensity >= 70)
        {
            insight.Warnings.Add("Expect physical pressure and possible card-heavy moments.");
        }
    }

    private static void AddRecommendations(TacticalInsight insight, Team selectedTeam, Team opponent)
    {
        if (opponent.Tactics.PressingIntensity >= 65)
        {
            insight.Recommendations.Add("Use balanced tempo or press-resistant midfielders to play through pressure.");
        }

        if (opponent.Tactics.DefensiveLine >= 65)
        {
            insight.Recommendations.Add("Use pace and direct attacks to exploit space behind their back line.");
        }

        if (opponent.Players.Any(player => player.Position == Position.Forward && GetOverall(player) >= 84))
        {
            insight.Recommendations.Add("Consider a lower defensive line or defensive mentality against their forwards.");
        }

        if (selectedTeam.Tactics.PressingIntensity >= 75)
        {
            insight.Recommendations.Add("High pressing can work, but monitor fatigue before halftime.");
        }

        if (insight.Recommendations.Count == 0)
        {
            insight.Recommendations.Add("Balanced mentality with controlled tempo is a safe opening setup.");
        }
    }

    private static int GetThreatRating(Player player)
    {
        return GetOverall(player) + player.CurrentForm / 5 + player.Morale / 5 + player.Traits.Count * 2;
    }

    private static int GetOverall(Player player)
    {
        return player.OverallRating > 0
            ? player.OverallRating
            : (int)Math.Round((player.Attack + player.Defense + player.Passing + player.Stamina + player.Finishing) / 5.0);
    }
}
