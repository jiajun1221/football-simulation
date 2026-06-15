using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Helpers;
using FootballSimulation.Wpf.Models;
using FootballSimulation.Wpf.Services;
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
        ScoreTextBlock.Text = CreateScoreText(match);
        ApplyTeamHeaderColors(match.HomeTeam, HomeTeamTextBlock, HomeTeamLogoBorder);
        ApplyTeamHeaderColors(match.AwayTeam, AwayTeamTextBlock, AwayTeamLogoBorder);
        HomePlayersTitleTextBlock.Text = $"{match.HomeTeam.Name} Players";
        AwayPlayersTitleTextBlock.Text = $"{match.AwayTeam.Name} Players";
        HomeTeamLogoImage.Source = CreateLogoSource(match.HomeTeam.Name);
        AwayTeamLogoImage.Source = CreateLogoSource(match.AwayTeam.Name);
        ResultHeaderTextBlock.Text = CreateResultHeader();
        NextButton.Content = IsPenaltyShootoutPending() ? "Continue to Penalty Shootout" : "Next";

        var totalGoals = match.HomeScore + match.AwayScore;
        HomeScorersContentControl.Content = CreateGoalSummaryPanel(match, match.HomeTeam, totalGoals, summary.ManOfTheMatch);
        AwayScorersContentControl.Content = CreateGoalSummaryPanel(match, match.AwayTeam, totalGoals, summary.ManOfTheMatch);
        HomePlayersItemsControl.ItemsSource = CreatePlayerRows(summary.PlayerPerformances, match.HomeTeam, summary.ManOfTheMatch);
        AwayPlayersItemsControl.ItemsSource = CreatePlayerRows(summary.PlayerPerformances, match.AwayTeam, summary.ManOfTheMatch);
        StatsComparisonItemsControl.ItemsSource = CreateStatComparisonRows(match);
    }

    private static void ApplyTeamHeaderColors(Team team, TextBlock teamTextBlock, Border logoBorder)
    {
        var colors = TeamColorService.GetPalette(team);
        teamTextBlock.Foreground = ToBrush(colors.PrimaryColor);
        logoBorder.Background = ToBrush(colors.PrimaryColor);
        logoBorder.BorderBrush = ToBrush(colors.BorderColor);
    }

    private string CreateScoreText(Match match)
    {
        var fixture = _state.CurrentFixture;
        if (fixture?.PenaltyHomeScore is not null && fixture.PenaltyAwayScore is not null)
        {
            var winner = string.IsNullOrWhiteSpace(fixture.WinningTeamName)
                ? fixture.PenaltyHomeScore > fixture.PenaltyAwayScore ? match.HomeTeam.Name : match.AwayTeam.Name
                : fixture.WinningTeamName;
            return $"{match.HomeScore} - {match.AwayScore}\n{winner} win {fixture.PenaltyHomeScore} - {fixture.PenaltyAwayScore} on penalties";
        }

        if (fixture?.ExtraTimeHomeScore is not null &&
            fixture.ExtraTimeAwayScore is not null &&
            match.HomeScore != match.AwayScore)
        {
            return $"{match.HomeScore} - {match.AwayScore} AET";
        }

        return $"{match.HomeScore} - {match.AwayScore}";
    }

    private static Brush ToBrush(string color)
    {
        return (Brush)new BrushConverter().ConvertFromString(color)!;
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsPenaltyShootoutPending())
        {
            _navigate(new PenaltyShootoutView(_state, _navigate));
            return;
        }

        TrophyCelebrationService.EnqueuePostMatchCelebrations(_state, TrophyCelebrationNextRoute.RoundResult);
        try
        {
            _navigate(_state.TrophyCelebrationQueue.Count > 0
                ? new ChampionCelebrationView(_state, _navigate, () => new RoundResultView(_state, _navigate))
                : new RoundResultView(_state, _navigate));
        }
        catch
        {
            _state.TrophyCelebrationQueue.Clear();
            _navigate(new RoundResultView(_state, _navigate));
        }
    }

    private string CreateResultHeader()
    {
        var fixture = _state.CurrentFixture;
        if (fixture is null)
        {
            return "FULL TIME";
        }

        var roundText = string.IsNullOrWhiteSpace(fixture.RoundName)
            ? $"Round {fixture.RoundNumber}"
            : fixture.RoundName;
        return $"FULL TIME - {CompetitionDisplayService.GetName(fixture.Competition)} - {roundText}";
    }

    private bool IsPenaltyShootoutPending()
    {
        var fixture = _state.CurrentFixture;
        var match = _state.CurrentMatch;
        return fixture is not null &&
            match is not null &&
            fixture.IsKnockout &&
            fixture.PenaltyHomeScore is null &&
            fixture.PenaltyAwayScore is null &&
            _state.CurrentLiveMatchSegment == LiveMatchSegment.ExtraTimeSecondHalf &&
            match.HomeScore == match.AwayScore;
    }

    private static ScorerPanel CreateGoalSummaryPanel(Match match, Team team, int totalGoals, PlayerMatchPerformance? manOfTheMatch)
    {
        var mode = totalGoals > 12
            ? ScorerSummaryMode.UltraCompact
            : totalGoals > 8
                ? ScorerSummaryMode.Compact
                : ScorerSummaryMode.Detailed;
        var scoringEvents = CreateAllowedScoringEvents(match)
            .Where(matchEvent => FindScoringTeam(matchEvent, match) == team.Name)
            .GroupBy(matchEvent => matchEvent.PrimaryPlayerName ?? "Unknown scorer")
            .Select(group => CreateScorerRow(
                group.Key,
                group.OrderBy(matchEvent => matchEvent.Minute).ToList(),
                mode,
                IsManOfTheMatch(group.Key, team, manOfTheMatch)))
            .OrderByDescending(row => row.GoalCount)
            .ThenBy(row => row.FirstMinute)
            .ThenBy(row => row.PlayerName)
            .ToList();

        return new ScorerPanel($"{team.Name} scorers", scoringEvents);
    }

    private static List<MatchEvent> CreateAllowedScoringEvents(Match match)
    {
        var scoringEvents = new List<MatchEvent>();

        foreach (var matchEvent in match.Events.OrderBy(matchEvent => matchEvent.Minute))
        {
            if (IsScoringEvent(matchEvent))
            {
                scoringEvents.Add(matchEvent);
                continue;
            }

            if (IsGoalDisallowedEvent(matchEvent))
            {
                var index = scoringEvents.FindLastIndex(scoringEvent =>
                    string.Equals(scoringEvent.PrimaryPlayerName, matchEvent.PrimaryPlayerName, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    scoringEvents.RemoveAt(index);
                }
            }
        }

        return scoringEvents;
    }

    private static ScorerRow CreateScorerRow(string playerName, List<MatchEvent> goals, ScorerSummaryMode mode, bool isManOfTheMatch)
    {
        var goalCount = goals.Count;
        var minutes = goals.Select(FormatGoalMinute).ToList();
        var minuteList = string.Join(", ", minutes);
        var achievement = CreateScorerAchievement(goalCount, isManOfTheMatch);
        var badgeText = mode == ScorerSummaryMode.UltraCompact
            ? $"x{goalCount}"
            : CreateGoalBadgeText(goalCount);
        var summary = mode switch
        {
            ScorerSummaryMode.Detailed => $"{playerName} ({minuteList})",
            ScorerSummaryMode.Compact => $"{playerName} x{goalCount}",
            _ => $"{playerName} x{goalCount}"
        };

        return new ScorerRow(
            playerName,
            summary,
            mode == ScorerSummaryMode.Compact ? minuteList : string.Empty,
            achievement,
            badgeText,
            goalCount >= 3 ? "#16A34A" : "#263754",
            goalCount >= 3 ? "#FFFFFF" : "#F7C948",
            goalCount,
            goals.Min(matchEvent => matchEvent.Minute),
            string.IsNullOrWhiteSpace(achievement)
                ? $"{playerName}: {minuteList}"
                : $"{playerName}: {minuteList} - {achievement}");
    }

    private static string FormatGoalMinute(MatchEvent matchEvent)
    {
        return !string.IsNullOrWhiteSpace(matchEvent.DisplayMinuteText)
            ? matchEvent.DisplayMinuteText
            : $"{matchEvent.Minute}'";
    }

    private static string CreateGoalBadgeText(int goalCount)
    {
        return goalCount switch
        {
            >= 4 => $"{FireIcon()} {goalCount}",
            3 => $"{SoccerBallIcon()}{SoccerBallIcon()}{SoccerBallIcon()}",
            2 => $"{SoccerBallIcon()}{SoccerBallIcon()}",
            _ => SoccerBallIcon()
        };
    }

    private static string CreateScorerAchievement(int goalCount, bool isManOfTheMatch)
    {
        var goalAchievement = goalCount switch
        {
            >= 4 => $"{FireIcon()} {goalCount} goals",
            3 => $"{SoccerBallIcon()}{SoccerBallIcon()}{SoccerBallIcon()} Hat-trick",
            2 => $"{SoccerBallIcon()}{SoccerBallIcon()} Brace",
            _ => string.Empty
        };
        var motmAchievement = isManOfTheMatch ? $"{StarIcon()} Man of the Match" : string.Empty;

        return string.Join("  ", new[] { goalAchievement, motmAchievement }.Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    private static bool IsManOfTheMatch(string playerName, Team team, PlayerMatchPerformance? manOfTheMatch)
    {
        return manOfTheMatch is not null &&
            string.Equals(manOfTheMatch.TeamName, team.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(manOfTheMatch.PlayerName, playerName, StringComparison.OrdinalIgnoreCase);
    }

    private BitmapImage? CreateLogoSource(string teamName)
    {
        return ClubLogoService.LoadClubLogo(teamName, _state.League?.LeagueId ?? _state.SelectedLeagueId);
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

    private static bool IsGoalDisallowedEvent(MatchEvent matchEvent)
    {
        return (matchEvent.EventType is EventType.VarDecision or EventType.Offside) &&
            (matchEvent.Description.Contains("goal ruled out", StringComparison.OrdinalIgnoreCase) ||
                matchEvent.Description.Contains("goal is ruled out", StringComparison.OrdinalIgnoreCase) ||
                matchEvent.Description.Contains("goal disallowed", StringComparison.OrdinalIgnoreCase));
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
        var nationality = player is null
            ? PlayerNationalityDisplayService.Resolve(new Player { Name = performance.PlayerName })
            : PlayerNationalityDisplayService.Resolve(player);
        var defensiveContributions = performance.Tackles + performance.Interceptions + performance.Blocks + performance.Clearances;

        return new PlayerPerformanceRow
        {
            PlayerName = performance.PlayerName,
            FlagImagePath = nationality.FlagImagePath,
            NationalityName = nationality.Name,
            PositionText = GetPositionText(performance.Position),
            OverallText = player is null ? string.Empty : $"OVR {GetOverallRating(player)}",
            GrowthText = player is null ? string.Empty : PlayerGrowthDisplayHelper.CreateGrowthText(player),
            RatingText = RatingDisplayHelper.CreateRatingText(performance.Rating),
            RatingBackground = RatingDisplayHelper.GetRatingBrush(performance.Rating),
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
            CardStatusBadges = player is null
                ? PlayerCardStatusBadgeHelper.Create(performance.YellowCards, performance.RedCards)
                : PlayerCardStatusBadgeHelper.Create(player, performance),
            GoalVisibility = performance.Goals > 0 ? Visibility.Visible : Visibility.Collapsed,
            AssistVisibility = performance.Assists > 0 ? Visibility.Visible : Visibility.Collapsed,
            DefensiveVisibility = defensiveContributions > 0 ? Visibility.Visible : Visibility.Collapsed,
            SaveVisibility = performance.Saves > 0 ? Visibility.Visible : Visibility.Collapsed,
            YellowVisibility = performance.YellowCards > 0 ? Visibility.Visible : Visibility.Collapsed,
            RedVisibility = performance.RedCards > 0 ? Visibility.Visible : Visibility.Collapsed,
            MotmIcon = StarIcon(),
            MotmVisibility = isMotm ? Visibility.Visible : Visibility.Collapsed,
            RowBackground = isMotm
                ? ThemeManager.GetBrushHex("TableCurrentClubBackground", "#5A3D12")
                : ThemeManager.GetBrushHex("AppSecondaryCardBackground", "#111827"),
            BorderBrush = isMotm
                ? ThemeManager.GetBrushHex("AppHighlightBrush", "#6B4A16")
                : ThemeManager.GetBrushHex("AppBorderBrush", "#243247"),
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

    private static int GetOverallRating(Player player)
    {
        return player.OverallRating > 0
            ? player.OverallRating
            : (int)Math.Round((player.Attack + player.Defense + player.Passing + player.Stamina + player.Finishing) / 5.0);
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

    private static string FireIcon()
    {
        return char.ConvertFromUtf32(0x1F525);
    }

    private sealed class PlayerPerformanceRow
    {
        public string PlayerName { get; init; } = string.Empty;
        public string FlagImagePath { get; init; } = "/Assets/Flags/default.png";
        public string NationalityName { get; init; } = "Unknown nationality";
        public string PositionText { get; init; } = string.Empty;
        public string OverallText { get; init; } = string.Empty;
        public string GrowthText { get; init; } = string.Empty;
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
        public IReadOnlyList<PlayerCardStatusBadge> CardStatusBadges { get; init; } = [];
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

    private sealed record ScorerPanel(string Title, List<ScorerRow> Scorers);

    private sealed record ScorerRow(
        string PlayerName,
        string SummaryText,
        string MinuteText,
        string AchievementText,
        string BadgeText,
        string BadgeBackground,
        string BadgeForeground,
        int GoalCount,
        int FirstMinute,
        string TooltipText)
    {
        public Visibility MinuteVisibility => string.IsNullOrWhiteSpace(MinuteText) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility AchievementVisibility => string.IsNullOrWhiteSpace(AchievementText) ? Visibility.Collapsed : Visibility.Visible;
    }

    private enum ScorerSummaryMode
    {
        Detailed,
        Compact,
        UltraCompact
    }
}
