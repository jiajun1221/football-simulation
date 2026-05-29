using System.Diagnostics;
using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Wpf.Services;

public static class PlayerNationalityDisplayService
{
    private static readonly Dictionary<string, NationalityDisplayInfo> CountryByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AR"] = Flag("AR", "Argentina", "argentina"),
        ["BE"] = Flag("BE", "Belgium", "belgium"),
        ["BR"] = Flag("BR", "Brazil", "brazil"),
        ["DE"] = Flag("DE", "Germany", "germany"),
        ["DK"] = Flag("DK", "Denmark", "denmark"),
        ["EC"] = Flag("EC", "Ecuador", "ecuador"),
        ["EG"] = Flag("EG", "Egypt", "egypt"),
        ["EN"] = Flag("GB-ENG", "England", "england"),
        ["ES"] = Flag("ES", "Spain", "spain"),
        ["FR"] = Flag("FR", "France", "france"),
        ["GB"] = Flag("GB-ENG", "England", "england"),
        ["GBR"] = Flag("GB-ENG", "England", "england"),
        ["GB-ENG"] = Flag("GB-ENG", "England", "england"),
        ["GB-NIR"] = Flag("GB-NIR", "Northern Ireland", "northern-ireland"),
        ["GB-SCT"] = Flag("GB-SCT", "Scotland", "scotland"),
        ["GB-WLS"] = Flag("GB-WLS", "Wales", "wales"),
        ["GH"] = Flag("GH", "Ghana", "ghana"),
        ["HR"] = Flag("HR", "Croatia", "croatia"),
        ["IT"] = Flag("IT", "Italy", "italy"),
        ["MA"] = Flag("MA", "Morocco", "morocco"),
        ["NG"] = Flag("NG", "Nigeria", "nigeria"),
        ["NIR"] = Flag("GB-NIR", "Northern Ireland", "northern-ireland"),
        ["NL"] = Flag("NL", "Netherlands", "netherlands"),
        ["NO"] = Flag("NO", "Norway", "norway"),
        ["PT"] = Flag("PT", "Portugal", "portugal"),
        ["SCT"] = Flag("GB-SCT", "Scotland", "scotland"),
        ["SCO"] = Flag("GB-SCT", "Scotland", "scotland"),
        ["SE"] = Flag("SE", "Sweden", "sweden"),
        ["SN"] = Flag("SN", "Senegal", "senegal"),
        ["UA"] = Flag("UA", "Ukraine", "ukraine"),
        ["US"] = Flag("US", "United States", "usa"),
        ["USA"] = Flag("US", "United States", "usa"),
        ["UY"] = Flag("UY", "Uruguay", "uruguay"),
        ["WA"] = Flag("GB-WLS", "Wales", "wales"),
        ["WAL"] = Flag("GB-WLS", "Wales", "wales")
    };

    private static readonly Dictionary<string, string> CodeByCountry = CountryByCode.Values
        .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.First().Code, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, NationalityDisplayInfo> CountryAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["British"] = Flag("GB-ENG", "England", "england"),
        ["Kingdom of Denmark"] = Flag("DK", "Denmark", "denmark"),
        ["Kingdom of the Netherlands"] = Flag("NL", "Netherlands", "netherlands"),
        ["United Kingdom"] = Flag("GB-ENG", "England", "england")
    };

    public static NationalityDisplayInfo Resolve(Player player)
    {
        if (ShouldBackfillNationality(player))
        {
            _ = PlayerNationalityDataService.TryApply(player);
        }

        if (!string.IsNullOrWhiteSpace(player.FlagImagePath) &&
            !IsDefaultOrGenericUnitedKingdomFlag(player.FlagImagePath))
        {
            return new NationalityDisplayInfo(
                string.IsNullOrWhiteSpace(player.NationalityCode) ? "UN" : player.NationalityCode.Trim(),
                string.IsNullOrWhiteSpace(player.NationalityName) ? "Unknown nationality" : player.NationalityName.Trim(),
                player.FlagImagePath.Trim());
        }

        if (!string.IsNullOrWhiteSpace(player.NationalityName) &&
            CountryAliases.TryGetValue(player.NationalityName.Trim(), out var aliasInfo))
        {
            return WithPlayerOverrides(player, aliasInfo);
        }

        if (!string.IsNullOrWhiteSpace(player.NationalityName) &&
            CodeByCountry.TryGetValue(player.NationalityName.Trim(), out var codeFromName) &&
            CountryByCode.TryGetValue(codeFromName, out var nameInfo))
        {
            return WithPlayerOverrides(player, nameInfo);
        }

        if (!string.IsNullOrWhiteSpace(player.NationalityCode) &&
            CountryByCode.TryGetValue(player.NationalityCode.Trim(), out var codeInfo))
        {
            return WithPlayerOverrides(player, codeInfo);
        }

        if (!string.IsNullOrWhiteSpace(player.Nationality) &&
            CodeByCountry.TryGetValue(player.Nationality.Trim(), out var legacyCode) &&
            CountryByCode.TryGetValue(legacyCode, out var legacyInfo))
        {
            return WithPlayerOverrides(player, legacyInfo);
        }

        Debug.WriteLine($"Missing nationality flag for player '{player.Name}'. Using default flag.");
        var fallback = Flag("UN", "Unknown nationality", "default");
        ApplyToPlayer(player, fallback);
        return fallback;
    }

    private static NationalityDisplayInfo WithPlayerOverrides(Player player, NationalityDisplayInfo info)
    {
        var resolved = info with
        {
            FlagImagePath = string.IsNullOrWhiteSpace(player.FlagImagePath) ||
                IsDefaultOrGenericUnitedKingdomFlag(player.FlagImagePath)
                    ? info.FlagImagePath
                    : player.FlagImagePath.Trim(),
            Name = string.IsNullOrWhiteSpace(player.NationalityName) ||
                player.NationalityName.Equals("Unknown nationality", StringComparison.OrdinalIgnoreCase) ||
                player.NationalityName.Equals("United Kingdom", StringComparison.OrdinalIgnoreCase)
                    ? info.Name
                    : player.NationalityName.Trim()
        };
        ApplyToPlayer(player, resolved);
        return resolved;
    }

    private static void ApplyToPlayer(Player player, NationalityDisplayInfo info)
    {
        player.NationalityCode = info.Code;
        player.NationalityName = info.Name;
        player.Nationality = info.Name;
        player.FlagImagePath = info.FlagImagePath;
    }

    private static bool ShouldBackfillNationality(Player player)
    {
        return string.IsNullOrWhiteSpace(player.NationalityCode) ||
            string.IsNullOrWhiteSpace(player.NationalityName) ||
            string.IsNullOrWhiteSpace(player.FlagImagePath) ||
            player.NationalityCode.Equals("UN", StringComparison.OrdinalIgnoreCase) ||
            player.NationalityCode.Equals("GBR", StringComparison.OrdinalIgnoreCase) ||
            player.NationalityName.Equals("Unknown nationality", StringComparison.OrdinalIgnoreCase) ||
            player.NationalityName.Equals("United Kingdom", StringComparison.OrdinalIgnoreCase) ||
            IsDefaultOrGenericUnitedKingdomFlag(player.FlagImagePath);
    }

    private static bool IsDefaultOrGenericUnitedKingdomFlag(string flagImagePath)
    {
        return flagImagePath.EndsWith("/default.png", StringComparison.OrdinalIgnoreCase) ||
            flagImagePath.EndsWith("\\default.png", StringComparison.OrdinalIgnoreCase) ||
            flagImagePath.EndsWith("/united-kingdom.png", StringComparison.OrdinalIgnoreCase) ||
            flagImagePath.EndsWith("\\united-kingdom.png", StringComparison.OrdinalIgnoreCase);
    }

    private static NationalityDisplayInfo Flag(string code, string name, string slug)
    {
        return new NationalityDisplayInfo(code, name, $"/Assets/Flags/{slug}.png");
    }
}

public sealed record NationalityDisplayInfo(string Code, string Name, string FlagImagePath);
