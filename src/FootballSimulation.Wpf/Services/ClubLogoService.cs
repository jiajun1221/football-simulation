using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;

namespace FootballSimulation.Wpf.Services;

public static class ClubLogoService
{
    private const string ClubsAssetPath = "Assets/Clubs";
    private const string DefaultLogoPath = "pack://application:,,,/Assets/Clubs/default.png";

    private static readonly Dictionary<string, string> ImportedLogoFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AFC Bournemouth"] = "AFC Bournemouth.png",
        ["Arsenal"] = "Arsenal FC.png",
        ["Aston Villa"] = "Aston Villa.png",
        ["Brentford"] = "Brentford FC.png",
        ["Brighton & Hove Albion"] = "Brighton Hove Albion.png",
        ["Burnley"] = "Burnley FC.png",
        ["Chelsea"] = "Chelsea FC.png",
        ["Crystal Palace"] = "Crystal Palace.png",
        ["Everton"] = "Everton FC.png",
        ["Fulham"] = "Fulham FC.png",
        ["Leeds United"] = "Leeds United.png",
        ["Liverpool"] = "Liverpool FC.png",
        ["Manchester City"] = "Manchester City.png",
        ["Manchester United"] = "Manchester United.png",
        ["Newcastle United"] = "Newcastle United.png",
        ["Nottingham Forest"] = "Nottingham Forest.png",
        ["Sunderland"] = "Sunderland AFC.png",
        ["Tottenham Hotspur"] = "Tottenham Hotspur.png",
        ["West Ham United"] = "West Ham United.png",
        ["Wolverhampton Wanderers"] = "Wolverhampton Wanderers.png"
    };

    public static string GetClubLogoPath(string clubName, string? leagueId = null)
    {
        foreach (var logoPath in GetLogoCandidatePaths(clubName, leagueId))
        {
            if (ResourceExists(logoPath))
            {
                return logoPath;
            }
        }

        return DefaultLogoPath;
    }

    public static BitmapImage? LoadClubLogo(string clubName, string? leagueId = null)
    {
        var logoPath = GetClubLogoPath(clubName, leagueId);
        return string.IsNullOrWhiteSpace(logoPath)
            ? null
            : new BitmapImage(new Uri(logoPath, UriKind.Absolute));
    }

    public static string CreateClubSlug(string clubName)
    {
        if (string.IsNullOrWhiteSpace(clubName))
        {
            return "default";
        }

        var normalized = clubName.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        var previousWasHyphen = false;

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasHyphen = false;
                continue;
            }

            if (!previousWasHyphen)
            {
                builder.Append('-');
                previousWasHyphen = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? "default" : slug;
    }

    private static IEnumerable<string> GetLogoCandidatePaths(string clubName, string? leagueId)
    {
        var slug = CreateClubSlug(clubName);
        if (!string.IsNullOrWhiteSpace(leagueId))
        {
            yield return $"pack://application:,,,/{ClubsAssetPath}/{leagueId}/{slug}.png";
        }

        yield return $"pack://application:,,,/{ClubsAssetPath}/{slug}.png";

        if (ImportedLogoFileNames.TryGetValue(clubName, out var importedFileName))
        {
            yield return CreatePackPath(importedFileName);
        }

        yield return DefaultLogoPath;
    }

    private static string CreatePackPath(string fileName)
    {
        var escapedFileName = Uri.EscapeDataString(fileName);
        return $"pack://application:,,,/{ClubsAssetPath}/{escapedFileName}";
    }

    private static bool ResourceExists(string packUri)
    {
        try
        {
            return Application.GetResourceStream(new Uri(packUri, UriKind.Absolute)) is not null;
        }
        catch
        {
            return false;
        }
    }
}
