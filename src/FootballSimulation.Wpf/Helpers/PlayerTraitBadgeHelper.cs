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
                TooltipText = string.Join(Environment.NewLine + Environment.NewLine, traitList.Select(CreateTooltipText)),
                Background = "#F1F5FF",
                Foreground = "#1E528F",
                FontFamily = "Segoe UI"
            });
        }

        return badges;
    }

    private static PlayerTraitBadge CreateTraitBadge(PlayerTrait trait)
    {
        var icon = PlayerTraitDisplayService.GetIcon(trait);
        var label = PlayerTraitDisplayService.GetLabel(trait);
        var description = PlayerTraitDisplayService.GetEffectDescription(trait);

        return new PlayerTraitBadge
        {
            Icon = icon,
            Label = label,
            Description = description,
            TooltipText = CreateTooltipText(icon, label, description)
        };
    }

    private static string CreateTooltipText(PlayerTrait trait)
    {
        return CreateTooltipText(
            PlayerTraitDisplayService.GetIcon(trait),
            PlayerTraitDisplayService.GetLabel(trait),
            PlayerTraitDisplayService.GetEffectDescription(trait));
    }

    private static string CreateTooltipText(string icon, string label, string description)
    {
        return $"{icon} {label}{Environment.NewLine}{description}";
    }
}
