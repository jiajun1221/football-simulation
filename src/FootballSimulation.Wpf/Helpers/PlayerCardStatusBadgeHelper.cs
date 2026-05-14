using FootballSimulation.Models;
using FootballSimulation.Wpf.Models;

namespace FootballSimulation.Wpf.Helpers;

public static class PlayerCardStatusBadgeHelper
{
    public static IReadOnlyList<PlayerCardStatusBadge> Create(Player player, PlayerMatchPerformance? performance = null)
    {
        var yellowCards = Math.Max(player.YellowCards, performance?.YellowCards ?? 0);
        var hasRedCard = player.IsSentOff || player.RedCardMinute.HasValue || (performance?.RedCards ?? 0) > 0;
        return Create(yellowCards, hasRedCard);
    }

    public static IReadOnlyList<PlayerCardStatusBadge> Create(int yellowCards, int redCards)
    {
        return Create(yellowCards, redCards > 0);
    }

    public static IReadOnlyList<PlayerCardStatusBadge> Create(int yellowCards, bool hasRedCard)
    {
        var badges = new List<PlayerCardStatusBadge>();
        if (yellowCards > 0)
        {
            badges.Add(new PlayerCardStatusBadge
            {
                Text = yellowCards > 1 ? $"{yellowCards}Y" : "Y",
                TooltipText = yellowCards > 1
                    ? "Second yellow: player is at serious dismissal risk."
                    : "Yellow card: substitution risk warning.",
                Background = "#FACC15",
                Foreground = "#1F2937",
                BorderBrush = "#FEF3C7"
            });
        }

        if (hasRedCard)
        {
            badges.Add(new PlayerCardStatusBadge
            {
                Text = "R",
                TooltipText = "Red card: player has been sent off.",
                Background = "#DC2626",
                Foreground = "#FFFFFF",
                BorderBrush = "#FCA5A5"
            });
        }

        return badges;
    }
}
