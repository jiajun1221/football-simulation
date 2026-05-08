using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Helpers;
using FootballSimulation.Wpf.Models;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Views;

public partial class MatchResultView : UserControl
{
    private const string ClubsAssetPath = "Assets/Clubs";
    private const string DefaultLogoPath = "pack://application:,,,/Assets/Clubs/default.png";

    private static readonly Dictionary<string, string> ImportedLogoFileNames = new()
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

    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;
    private readonly PostMatchAnalysisService _postMatchAnalysisService = new();

    public MatchResultView(GameFlowState state, Action<UserControl> navigate)
    {
        InitializeComponent();

        _state = state;
        _navigate = navigate;

        LoadResult();
    }

    private void LoadResult()
    {
        if (_state.CurrentMatch is null)
        {
            return;
        }

        var match = _state.CurrentMatch;
        var summary = _postMatchAnalysisService.CreateSummary(match);

        HomeTeamTextBlock.Text = match.HomeTeam.Name;
        AwayTeamTextBlock.Text = match.AwayTeam.Name;
        ScoreTextBlock.Text = $"{match.HomeScore} - {match.AwayScore}";
        HomePlayersTitleTextBlock.Text = $"{match.HomeTeam.Name} Players";
        AwayPlayersTitleTextBlock.Text = $"{match.AwayTeam.Name} Players";
        HomeTeamLogoImage.Source = CreateLogoSource(match.HomeTeam.Name);
        AwayTeamLogoImage.Source = CreateLogoSource(match.AwayTeam.Name);

        HomeScorersItemsControl.ItemsSource = CreateGoalSummaryRows(match, match.HomeTeam);
        AwayScorersItemsControl.ItemsSource = CreateGoalSummaryRows(match, match.AwayTeam);
        HomePlayersItemsControl.ItemsSource = CreatePlayerRows(summary.PlayerPerformances, match.HomeTeam, summary.ManOfTheMatch);
        AwayPlayersItemsControl.ItemsSource = CreatePlayerRows(summary.PlayerPerformances, match.AwayTeam, summary.ManOfTheMatch);
        StatsComparisonItemsControl.ItemsSource = CreateStatComparisonRows(match);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        _navigate(new RoundResultView(_state, _navigate));
    }

    private static List<ScorerRow> CreateGoalSummaryRows(Match match, Team team)
    {
        var scoringEvents = match.Events
            .Where(IsScoringEvent)
            .Where(matchEvent => FindScoringTeam(matchEvent, match) == team.Name)
            .OrderBy(matchEvent => matchEvent.Minute)
            .Select(matchEvent => new ScorerRow(
                $"{matchEvent.Minute}'",
                matchEvent.PrimaryPlayerName ?? "Unknown scorer"))
            .ToList();

        return scoringEvents.Count == 0 ? [] : scoringEvents;
    }

    private static BitmapImage? CreateLogoSource(string teamName)
    {
        foreach (var logoPath in GetLogoCandidatePaths(teamName))
        {
            if (ResourceExists(logoPath))
            {
                return new BitmapImage(new Uri(logoPath, UriKind.Absolute));
            }
        }

        return null;
    }

    private static IEnumerable<string> GetLogoCandidatePaths(string teamName)
    {
        if (ImportedLogoFileNames.TryGetValue(teamName, out var importedFileName))
        {
            yield return CreatePackPath(importedFileName);
        }

        yield return TeamSelectionView.GetClubLogoPath(teamName);
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

    private static bool IsScoringEvent(MatchEvent matchEvent)
    {
        return matchEvent.EventType == EventType.Goal
            || matchEvent.EventType == EventType.WonderGoal
            || (matchEvent.EventType == EventType.Penalty
                && matchEvent.Description.Contains("scores", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindScoringTeam(MatchEvent matchEvent, Match match)
    {
        if (matchEvent.Description.Contains($"for {match.HomeTeam.Name}", StringComparison.OrdinalIgnoreCase))
        {
            return match.HomeTeam.Name;
        }

        if (matchEvent.Description.Contains($"for {match.AwayTeam.Name}", StringComparison.OrdinalIgnoreCase))
        {
            return match.AwayTeam.Name;
        }

        return string.Empty;
    }

    private static List<PlayerPerformanceRow> CreatePlayerRows(
        List<PlayerMatchPerformance> performances,
        Team team,
        PlayerMatchPerformance? manOfTheMatch)
    {
        return performances
            .Where(performance => performance.TeamName == team.Name)
            .OrderByDescending(performance => performance.Rating)
            .ThenByDescending(performance => performance.Goals + performance.Assists)
            .ThenByDescending(performance => performance.Saves)
            .ThenBy(performance => performance.PlayerName)
            .Select(performance => CreatePlayerRow(performance, team, manOfTheMatch))
            .ToList();
    }

    private static PlayerPerformanceRow CreatePlayerRow(
        PlayerMatchPerformance performance,
        Team team,
        PlayerMatchPerformance? manOfTheMatch)
    {
        var isMotm = manOfTheMatch is not null
            && manOfTheMatch.PlayerName == performance.PlayerName
            && manOfTheMatch.TeamName == performance.TeamName;
        var player = team.Players.Concat(team.Substitutes)
            .FirstOrDefault(candidate => string.Equals(candidate.Name, performance.PlayerName, StringComparison.OrdinalIgnoreCase));
        var formBadge = PlayerFormBadgeHelper.Create(player?.FormStatus ?? PlayerFormStatus.Average);
        var defensiveContributions = performance.Tackles + performance.Interceptions + performance.Blocks + performance.Clearances;

        return new PlayerPerformanceRow
        {
            PlayerName = performance.PlayerName,
            PositionText = GetPositionText(performance.Position),
            RatingText = performance.Rating.ToString("0.0"),
            RatingBackground = GetRatingBackground(performance.Rating),
            StatusText = GetSubStatus(performance),
            GoalText = performance.Goals > 1 ? $"{SoccerBallIcon()} {performance.Goals}" : SoccerBallIcon(),
            AssistText = performance.Assists > 1 ? $"{AssistIcon()} {performance.Assists}" : AssistIcon(),
            DefensiveText = defensiveContributions > 1 ? $"{ShieldIcon()} {defensiveContributions}" : ShieldIcon(),
            SaveText = performance.Saves > 1 ? $"{GloveIcon()} {performance.Saves}" : GloveIcon(),
            YellowText = performance.YellowCards > 1 ? $"Y {performance.YellowCards}" : "Y",
            RedText = performance.RedCards > 1 ? $"R {performance.RedCards}" : "R",
            FormBadgeText = formBadge.Text,
            FormBadgeBackground = formBadge.Background,
            FormBadgeForeground = formBadge.Foreground,
            TraitBadges = PlayerTraitBadgeHelper.Create(player?.Traits ?? []),
            GoalVisibility = performance.Goals > 0 ? Visibility.Visible : Visibility.Collapsed,
            AssistVisibility = performance.Assists > 0 ? Visibility.Visible : Visibility.Collapsed,
            DefensiveVisibility = defensiveContributions > 0 ? Visibility.Visible : Visibility.Collapsed,
            SaveVisibility = performance.Saves > 0 ? Visibility.Visible : Visibility.Collapsed,
            YellowVisibility = performance.YellowCards > 0 ? Visibility.Visible : Visibility.Collapsed,
            RedVisibility = performance.RedCards > 0 ? Visibility.Visible : Visibility.Collapsed,
            MotmIcon = StarIcon(),
            MotmVisibility = isMotm ? Visibility.Visible : Visibility.Collapsed,
            RowBackground = isMotm ? "#FFF8D6" : "#F8FAFC",
            BorderBrush = isMotm ? "#E3A500" : "#E2E8F0",
            BorderThickness = isMotm ? new Thickness(2) : new Thickness(1)
        };
    }

    private static List<StatComparisonRow> CreateStatComparisonRows(Match match)
    {
        return
        [
            new("Possession", $"{match.HomeStats.PossessionPercentage:0.0}%", $"{match.AwayStats.PossessionPercentage:0.0}%"),
            new("Shots", match.HomeStats.TotalShots.ToString(), match.AwayStats.TotalShots.ToString()),
            new("Shots on Target", match.HomeStats.ShotsOnTarget.ToString(), match.AwayStats.ShotsOnTarget.ToString()),
            new("Passes", match.HomeStats.Passes.ToString(), match.AwayStats.Passes.ToString()),
            new("Pass Accuracy", $"{match.HomeStats.PassAccuracyPercentage:0.0}%", $"{match.AwayStats.PassAccuracyPercentage:0.0}%"),
            new("xG", match.HomeStats.ExpectedGoals.ToString("0.0"), match.AwayStats.ExpectedGoals.ToString("0.0")),
            new("Fouls", match.HomeStats.Fouls.ToString(), match.AwayStats.Fouls.ToString()),
            new("Yellow Cards", match.HomeStats.YellowCards.ToString(), match.AwayStats.YellowCards.ToString()),
            new("Red Cards", match.HomeStats.RedCards.ToString(), match.AwayStats.RedCards.ToString()),
            new("Offsides", match.HomeStats.Offsides.ToString(), match.AwayStats.Offsides.ToString()),
            new("Corners", match.HomeStats.Corners.ToString(), match.AwayStats.Corners.ToString())
        ];
    }

    private static string GetSubStatus(PlayerMatchPerformance performance)
    {
        if (performance.WasSubbedOn)
        {
            return $"Sub on {performance.SubstitutionMinute}'";
        }

        if (performance.WasSubbedOff)
        {
            return $"Sub off {performance.SubstitutionMinute}'";
        }

        return performance.WasSubstitute ? "Bench" : "Starter";
    }

    private static string GetPositionText(Position position)
    {
        return position switch
        {
            Position.Goalkeeper => "GK",
            Position.Defender => "DEF",
            Position.Midfielder => "MID",
            Position.Forward => "FWD",
            _ => position.ToString()
        };
    }

    private static string GetRatingBackground(double rating)
    {
        return rating switch
        {
            >= 9.0 => "#166534",
            >= 8.0 => "#22C55E",
            >= 7.0 => "#FACC15",
            >= 6.0 => "#FB923C",
            _ => "#EF4444"
        };
    }

    private static string SoccerBallIcon()
    {
        return char.ConvertFromUtf32(0x26BD);
    }

    private static string AssistIcon()
    {
        return char.ConvertFromUtf32(0x1F3AF);
    }

    private static string ShieldIcon()
    {
        return char.ConvertFromUtf32(0x1F6E1);
    }

    private static string GloveIcon()
    {
        return char.ConvertFromUtf32(0x1F9E4);
    }

    private static string StarIcon()
    {
        return char.ConvertFromUtf32(0x2605);
    }

    private sealed class PlayerPerformanceRow
    {
        public string PlayerName { get; init; } = string.Empty;
        public string PositionText { get; init; } = string.Empty;
        public string RatingText { get; init; } = string.Empty;
        public string RatingBackground { get; init; } = "#102033";
        public string StatusText { get; init; } = string.Empty;
        public string GoalText { get; init; } = string.Empty;
        public string AssistText { get; init; } = string.Empty;
        public string DefensiveText { get; init; } = string.Empty;
        public string SaveText { get; init; } = string.Empty;
        public string YellowText { get; init; } = string.Empty;
        public string RedText { get; init; } = string.Empty;
        public string MotmIcon { get; init; } = string.Empty;
        public string FormBadgeText { get; init; } = string.Empty;
        public string FormBadgeBackground { get; init; } = "#FACC15";
        public string FormBadgeForeground { get; init; } = "#1F2937";
        public IReadOnlyList<PlayerTraitBadge> TraitBadges { get; init; } = [];
        public Visibility GoalVisibility { get; init; }
        public Visibility AssistVisibility { get; init; }
        public Visibility DefensiveVisibility { get; init; }
        public Visibility SaveVisibility { get; init; }
        public Visibility YellowVisibility { get; init; }
        public Visibility RedVisibility { get; init; }
        public Visibility MotmVisibility { get; init; }
        public string RowBackground { get; init; } = "#F8FAFC";
        public string BorderBrush { get; init; } = "#E2E8F0";
        public Thickness BorderThickness { get; init; } = new(1);
    }

    private sealed record StatComparisonRow(string Label, string HomeValue, string AwayValue);

    private sealed record ScorerRow(string MinuteText, string PlayerName);
}
