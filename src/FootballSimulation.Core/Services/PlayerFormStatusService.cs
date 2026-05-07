using FootballSimulation.Models;

namespace FootballSimulation.Services;

public class PlayerFormStatusService
{
    public void UpdateMatchPlayerFormStatuses(Match match)
    {
        foreach (var player in match.HomeTeam.Players.Concat(match.HomeTeam.Substitutes))
        {
            UpdatePlayerFormStatus(match, match.HomeTeam, player);
        }

        foreach (var player in match.AwayTeam.Players.Concat(match.AwayTeam.Substitutes))
        {
            UpdatePlayerFormStatus(match, match.AwayTeam, player);
        }
    }

    public void UpdateLiveMatchPlayerFormStatuses(Match match)
    {
        foreach (var player in match.HomeTeam.Players.Concat(match.HomeTeam.Substitutes))
        {
            UpdatePlayerLiveFormStatus(match, match.HomeTeam, player);
        }

        foreach (var player in match.AwayTeam.Players.Concat(match.AwayTeam.Substitutes))
        {
            UpdatePlayerLiveFormStatus(match, match.AwayTeam, player);
        }
    }

    public static PlayerFormStatus FromLoadedForm(string? form, int currentForm)
    {
        var normalizedForm = form?.Trim().Replace(" ", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        return normalizedForm switch
        {
            "hot" or "excellent" => PlayerFormStatus.Excellent,
            "good" => PlayerFormStatus.Good,
            "poor" => PlayerFormStatus.Poor,
            "verypoor" or "invisible" => PlayerFormStatus.VeryPoor,
            _ => FromFormScore(currentForm)
        };
    }

    public static string ToDisplayText(PlayerFormStatus status)
    {
        return status switch
        {
            PlayerFormStatus.Excellent => "Excellent",
            PlayerFormStatus.Good => "Good",
            PlayerFormStatus.Poor => "Poor",
            PlayerFormStatus.VeryPoor => "Very Poor",
            _ => "Average"
        };
    }

    public static int ToCurrentForm(PlayerFormStatus status)
    {
        return status switch
        {
            PlayerFormStatus.Excellent => 85,
            PlayerFormStatus.Good => 70,
            PlayerFormStatus.Poor => 35,
            PlayerFormStatus.VeryPoor => 20,
            _ => 50
        };
    }

    private void UpdatePlayerFormStatus(Match match, Team team, Player player)
    {
        var performance = match.PlayerPerformances.FirstOrDefault(candidate =>
            candidate.TeamName == team.Name &&
            candidate.PlayerName == player.Name);

        if (performance is null)
        {
            return;
        }

        ApplyFormStatus(player, CalculateFormStatus(player, performance));
    }

    private void UpdatePlayerLiveFormStatus(Match match, Team team, Player player)
    {
        var performance = match.PlayerPerformances.FirstOrDefault(candidate =>
            candidate.TeamName == team.Name &&
            candidate.PlayerName == player.Name);

        if (performance is null)
        {
            return;
        }

        ApplyFormStatus(player, CalculateFormStatus(player, performance));
    }

    private static void ApplyFormStatus(Player player, PlayerFormStatus status)
    {
        player.FormStatus = status;
        player.Form = ToDisplayText(status);
        player.CurrentForm = ToCurrentForm(status);
    }

    public static PlayerFormStatus CalculateFormStatus(Player player, PlayerMatchPerformance performance)
    {
        var score = CalculateFormScore(player, performance);

        return score switch
        {
            >= 9.0 => PlayerFormStatus.Excellent,
            >= 7.5 => PlayerFormStatus.Good,
            >= 6.0 => PlayerFormStatus.Average,
            >= 5.0 => PlayerFormStatus.Poor,
            _ => PlayerFormStatus.VeryPoor
        };
    }

    private static double CalculateFormScore(Player player, PlayerMatchPerformance performance)
    {
        var defensiveContribution = performance.Tackles +
            performance.Interceptions +
            performance.Blocks +
            performance.Clearances;
        var positiveImpact = Math.Min(
            0.40,
            performance.Goals * 0.08 +
            performance.Assists * 0.06 +
            performance.KeyPasses * 0.02 +
            performance.ShotsOnTarget * 0.02 +
            defensiveContribution * 0.015 +
            performance.Saves * 0.02);
        var mistakes = performance.Offsides +
            performance.Fouls +
            performance.YellowCards +
            performance.RedCards * 2 +
            performance.Injuries;
        var negativeImpact = Math.Min(0.50, mistakes * 0.06);

        var staminaPenalty = player.Stamina switch
        {
            < 20 => 0.30,
            < 35 => 0.20,
            < 60 => 0.10,
            _ => 0.0
        };

        if (player.IsSentOff || performance.RedCards > 0)
        {
            staminaPenalty += 0.25;
        }

        if (player.IsInjured || performance.Injuries > 0)
        {
            staminaPenalty += 0.15;
        }

        return Math.Clamp(performance.Rating + positiveImpact - negativeImpact - staminaPenalty, 0.0, 10.0);
    }

    private static PlayerFormStatus FromFormScore(int currentForm)
    {
        return currentForm switch
        {
            >= 80 => PlayerFormStatus.Excellent,
            >= 65 => PlayerFormStatus.Good,
            >= 40 => PlayerFormStatus.Average,
            >= 25 => PlayerFormStatus.Poor,
            _ => PlayerFormStatus.VeryPoor
        };
    }
}
