using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Models;

namespace FootballSimulation.Wpf.Helpers;

public static class PlayerTraitBadgeHelper
{
    private const int DefaultMaxVisibleTraits = 3;

    public static IReadOnlyList<PlayerTraitBadge> Create(IEnumerable<PlayerTrait> traits, int maxVisibleTraits = DefaultMaxVisibleTraits)
    {
        var traitList = traits.Distinct().ToList();
        if (traitList.Count == 0)
        {
            return [];
        }

        var badges = traitList
            .Take(maxVisibleTraits)
            .Select(CreateTraitBadge)
            .ToList();

        var extraCount = traitList.Count - maxVisibleTraits;
        if (extraCount > 0)
        {
            badges.Add(new PlayerTraitBadge
            {
                Icon = $"+{extraCount}",
                TooltipText = string.Join(Environment.NewLine + Environment.NewLine, traitList.Skip(maxVisibleTraits).Select(CreateTooltipText)),
                Background = "#F1F5FF",
                Foreground = "#1E528F",
                FontFamily = "Segoe UI"
            });
        }

        return badges;
    }

    private static PlayerTraitBadge CreateTraitBadge(PlayerTrait trait)
    {
        return new PlayerTraitBadge
        {
            Icon = PlayerTraitDisplayService.GetIcon(trait),
            TooltipText = CreateTooltipText(trait)
        };
    }

    private static string CreateTooltipText(PlayerTrait trait)
    {
        return $"{PlayerTraitDisplayService.GetIcon(trait)} {PlayerTraitDisplayService.GetLabel(trait)}{Environment.NewLine}{PlayerTraitDisplayService.GetEffectDescription(trait)}";
    }
}
