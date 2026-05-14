using System.Globalization;
using FootballSimulation.Models;

namespace FootballSimulation.Wpf.Services;

public static class TeamColorService
{
    private const string DefaultPrimaryColor = "#2563EB";
    private const string DefaultSecondaryColor = "#E0ECFF";

    private static readonly IReadOnlyDictionary<string, (string Primary, string Secondary)> TeamColors = new Dictionary<string, (string Primary, string Secondary)>(StringComparer.OrdinalIgnoreCase)
    {
        ["AFC Bournemouth"] = ("#D71920", "#111827"),
        ["Arsenal"] = ("#E11D48", "#FFFFFF"),
        ["Aston Villa"] = ("#7A003C", "#95BFE5"),
        ["Brentford"] = ("#E30613", "#FFFFFF"),
        ["Brighton & Hove Albion"] = ("#0057B8", "#FFFFFF"),
        ["Burnley"] = ("#6C1D45", "#99D6EA"),
        ["Chelsea"] = ("#2563EB", "#FFFFFF"),
        ["Crystal Palace"] = ("#1D4ED8", "#DC2626"),
        ["Everton"] = ("#1D4ED8", "#FFFFFF"),
        ["Fulham"] = ("#F8FAFC", "#111827"),
        ["Leeds United"] = ("#F8FAFC", "#1D4ED8"),
        ["Liverpool"] = ("#DC2626", "#FFFFFF"),
        ["Manchester City"] = ("#38BDF8", "#FFFFFF"),
        ["Manchester United"] = ("#EF4444", "#FACC15"),
        ["Newcastle United"] = ("#111827", "#F8FAFC"),
        ["Newcastle"] = ("#111827", "#F8FAFC"),
        ["Nottingham Forest"] = ("#DC2626", "#FFFFFF"),
        ["Sunderland"] = ("#DC2626", "#FFFFFF"),
        ["Tottenham Hotspur"] = ("#F8FAFC", "#111827"),
        ["Tottenham"] = ("#F8FAFC", "#111827"),
        ["West Ham United"] = ("#7A263A", "#7DD3FC"),
        ["Wolverhampton Wanderers"] = ("#F59E0B", "#111827"),
        ["Blue Hawks"] = ("#2563EB", "#FFFFFF"),
        ["Red Lions"] = ("#DC2626", "#FFFFFF")
    };

    public static TeamColorPalette GetPalette(Team? team)
    {
        return GetPalette(team?.Name);
    }

    public static TeamColorPalette GetPalette(string? teamName)
    {
        (string Primary, string Secondary) colors = !string.IsNullOrWhiteSpace(teamName) && TeamColors.TryGetValue(teamName, out var configured)
            ? configured
            : (DefaultPrimaryColor, DefaultSecondaryColor);

        return CreatePalette(colors.Primary, colors.Secondary);
    }

    public static MatchTeamColorPalettes GetMatchPalettes(Team? homeTeam, Team? awayTeam)
    {
        var homeColors = GetConfiguredColors(homeTeam?.Name);
        var awayColors = GetConfiguredColors(awayTeam?.Name);
        var homePalette = CreatePalette(homeColors.Primary, homeColors.Secondary);
        var awayPrimary = awayColors.Primary;
        var awaySecondary = awayColors.Secondary;

        if (AreTooSimilar(homePalette.PrimaryColor, awayPrimary))
        {
            awayPrimary = awaySecondary;
        }

        if (AreTooSimilar(homePalette.PrimaryColor, awayPrimary))
        {
            awayPrimary = ChooseFallbackContrastColor(homePalette.PrimaryColor);
            awaySecondary = GetReadableTextColor(awayPrimary);
        }

        return new MatchTeamColorPalettes(
            homePalette,
            CreatePalette(awayPrimary, awaySecondary));
    }

    public static string GetReadableTextColor(string backgroundColor)
    {
        return IsLightColor(backgroundColor) ? "#0F172A" : "#FFFFFF";
    }

    private static (string Primary, string Secondary) GetConfiguredColors(string? teamName)
    {
        return !string.IsNullOrWhiteSpace(teamName) && TeamColors.TryGetValue(teamName, out var configured)
            ? configured
            : (DefaultPrimaryColor, DefaultSecondaryColor);
    }

    private static TeamColorPalette CreatePalette(string primaryColor, string secondaryColor)
    {
        var isLight = IsLightColor(primaryColor);
        var textColor = isLight ? "#0F172A" : "#FFFFFF";
        var borderColor = isLight ? Darken(primaryColor, 0.42) : Lighten(primaryColor, 0.30);
        var subtleBackground = isLight ? "#FFFFFF" : Lighten(primaryColor, 0.82);
        var selectedGlowColor = isLight ? Darken(primaryColor, 0.25) : Lighten(primaryColor, 0.52);

        return new TeamColorPalette(
            PrimaryColor: primaryColor,
            SecondaryColor: secondaryColor,
            TextColor: textColor,
            BorderColor: borderColor,
            SubtleBackgroundColor: subtleBackground,
            SelectedGlowColor: selectedGlowColor,
            IsLight: isLight);
    }

    private static bool AreTooSimilar(string firstColor, string secondColor)
    {
        var (firstRed, firstGreen, firstBlue) = ParseHex(firstColor);
        var (secondRed, secondGreen, secondBlue) = ParseHex(secondColor);
        var colorDistance = Math.Sqrt(
            Math.Pow(firstRed - secondRed, 2) +
            Math.Pow(firstGreen - secondGreen, 2) +
            Math.Pow(firstBlue - secondBlue, 2));
        var brightnessDifference = Math.Abs(GetBrightness(firstColor) - GetBrightness(secondColor));

        return colorDistance < 115 || brightnessDifference < 0.16 && colorDistance < 150;
    }

    private static bool IsLightColor(string hexColor)
    {
        return GetBrightness(hexColor) > 0.66;
    }

    private static double GetBrightness(string hexColor)
    {
        var (red, green, blue) = ParseHex(hexColor);
        return (0.2126 * red + 0.7152 * green + 0.0722 * blue) / 255.0;
    }

    private static string ChooseFallbackContrastColor(string homePrimaryColor)
    {
        var candidates = new[] { "#F59E0B", "#111827", "#F8FAFC", "#7C3AED", "#10B981" };
        return candidates
            .OrderByDescending(candidate => GetColorDistance(homePrimaryColor, candidate))
            .First();
    }

    private static double GetColorDistance(string firstColor, string secondColor)
    {
        var (firstRed, firstGreen, firstBlue) = ParseHex(firstColor);
        var (secondRed, secondGreen, secondBlue) = ParseHex(secondColor);
        return Math.Sqrt(
            Math.Pow(firstRed - secondRed, 2) +
            Math.Pow(firstGreen - secondGreen, 2) +
            Math.Pow(firstBlue - secondBlue, 2));
    }

    private static string Lighten(string hexColor, double amount)
    {
        var (red, green, blue) = ParseHex(hexColor);
        return ToHex(
            red + (255 - red) * amount,
            green + (255 - green) * amount,
            blue + (255 - blue) * amount);
    }

    private static string Darken(string hexColor, double amount)
    {
        var (red, green, blue) = ParseHex(hexColor);
        return ToHex(red * (1 - amount), green * (1 - amount), blue * (1 - amount));
    }

    private static (int Red, int Green, int Blue) ParseHex(string hexColor)
    {
        var value = hexColor.Trim().TrimStart('#');
        if (value.Length != 6)
        {
            value = DefaultPrimaryColor.TrimStart('#');
        }

        return (
            int.Parse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(value.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(value.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    private static string ToHex(double red, double green, double blue)
    {
        return $"#{ClampToByte(red):X2}{ClampToByte(green):X2}{ClampToByte(blue):X2}";
    }

    private static int ClampToByte(double value)
    {
        return (int)Math.Clamp(Math.Round(value), 0, 255);
    }
}

public sealed record TeamColorPalette(
    string PrimaryColor,
    string SecondaryColor,
    string TextColor,
    string BorderColor,
    string SubtleBackgroundColor,
    string SelectedGlowColor,
    bool IsLight);

public sealed record MatchTeamColorPalettes(
    TeamColorPalette Home,
    TeamColorPalette Away);
