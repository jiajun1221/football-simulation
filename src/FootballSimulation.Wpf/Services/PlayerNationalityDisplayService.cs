using System.Diagnostics;
using FootballSimulation.Models;

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
        ["GB"] = Flag("GB", "United Kingdom", "united-kingdom"),
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

    public static NationalityDisplayInfo Resolve(Player player)
    {
        if (!string.IsNullOrWhiteSpace(player.FlagImagePath) &&
            !player.FlagImagePath.EndsWith("/default.png", StringComparison.OrdinalIgnoreCase) &&
            !player.FlagImagePath.EndsWith("\\default.png", StringComparison.OrdinalIgnoreCase))
        {
            return new NationalityDisplayInfo(
                string.IsNullOrWhiteSpace(player.NationalityCode) ? "UN" : player.NationalityCode.Trim(),
                string.IsNullOrWhiteSpace(player.NationalityName) ? "Unknown nationality" : player.NationalityName.Trim(),
                player.FlagImagePath.Trim());
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
            FlagImagePath = string.IsNullOrWhiteSpace(player.FlagImagePath) ? info.FlagImagePath : player.FlagImagePath.Trim(),
            Name = string.IsNullOrWhiteSpace(player.NationalityName) ? info.Name : player.NationalityName.Trim()
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

    private static NationalityDisplayInfo Flag(string code, string name, string slug)
    {
        return new NationalityDisplayInfo(code, name, $"/Assets/Flags/{slug}.png");
    }
}

public sealed record NationalityDisplayInfo(string Code, string Name, string FlagImagePath);
