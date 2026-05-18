using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Helpers;
using FootballSimulation.Wpf.Services;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Views;

public partial class LeaguePlayerStatsView : UserControl
{
    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;
    private readonly PlayerSeasonStatsService _statsService = new();
    private StatsCategory _currentCategory = StatsCategory.Goals;

    public LeaguePlayerStatsView(GameFlowState state, Action<UserControl> navigate)
    {
        InitializeComponent();
        _state = state;
        _navigate = navigate;
        LoadStats();
    }

    private void LoadStats()
    {
        if (_state.League is null)
        {
            return;
        }

        if (_state.League.PlayerStats.Count == 0)
        {
            _statsService.RebuildLeagueSeasonStats(_state.League);
        }

        LeagueSubtitleTextBlock.Text = $"League: {_state.League.Name}";
        SelectCategory(StatsCategory.Goals);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _navigate(new DashboardView(_state, _navigate));
    }

    private void CategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } &&
            Enum.TryParse<StatsCategory>(tag, out var category))
        {
            SelectCategory(category);
        }
    }

    private void SelectCategory(StatsCategory category)
    {
        if (_state.League is null)
        {
            return;
        }

        _currentCategory = category;
        UpdateCategoryButtons();
        UpdateColumnVisibility();

        StatsDataGrid.ItemsSource = CreateRows(_state.League.PlayerStats, category)
            .Select((row, index) => row with { Rank = index + 1 })
            .ToList();
    }

    private IEnumerable<PlayerStatsRow> CreateRows(IEnumerable<PlayerSeasonStats> stats, StatsCategory category)
    {
        var rows = stats
            .Where(stat => stat.Appearances > 0)
            .Select(CreateRow);

        return category switch
        {
            StatsCategory.Goals => rows
                .OrderByDescending(row => row.Goals)
                .ThenByDescending(row => row.Assists)
                .ThenByDescending(row => row.AverageRating)
                .ThenBy(row => row.PlayerName),
            StatsCategory.Assists => rows
                .OrderByDescending(row => row.Assists)
                .ThenByDescending(row => row.Goals)
                .ThenByDescending(row => row.AverageRating)
                .ThenBy(row => row.PlayerName),
            StatsCategory.Saves => rows
                .Where(row => row.Position == Position.Goalkeeper)
                .OrderByDescending(row => row.Saves)
                .ThenByDescending(row => row.AverageRating)
                .ThenBy(row => row.GoalsConceded)
                .ThenBy(row => row.PlayerName),
            StatsCategory.CleanSheets => rows
                .Where(row => row.Position is Position.Goalkeeper or Position.Defender)
                .OrderByDescending(row => row.CleanSheets)
                .ThenBy(row => row.GoalsConceded)
                .ThenByDescending(row => row.AverageRating)
                .ThenBy(row => row.PlayerName),
            StatsCategory.YellowCards => rows
                .OrderByDescending(row => row.YellowCards)
                .ThenByDescending(row => row.RedCards)
                .ThenBy(row => row.PlayerName),
            StatsCategory.RedCards => rows
                .OrderByDescending(row => row.RedCards)
                .ThenByDescending(row => row.YellowCards)
                .ThenBy(row => row.PlayerName),
            StatsCategory.Ratings => rows
                .OrderByDescending(row => row.AverageRating)
                .ThenByDescending(row => row.Matches)
                .ThenByDescending(row => row.Goals)
                .ThenByDescending(row => row.Assists)
                .ThenBy(row => row.PlayerName),
            StatsCategory.Appearances => rows
                .OrderByDescending(row => row.Matches)
                .ThenByDescending(row => row.Starts)
                .ThenByDescending(row => row.MinutesPlayed)
                .ThenBy(row => row.PlayerName),
            _ => rows
        };
    }

    private PlayerStatsRow CreateRow(PlayerSeasonStats stat)
    {
        var player = FindPlayer(stat);
        var formBadge = PlayerFormBadgeHelper.Create(player?.FormStatus ?? PlayerFormStatus.Average);
        var selectedTeamName = _state.SelectedTeam?.Name ?? string.Empty;

        return new PlayerStatsRow(
            Rank: 0,
            PlayerName: stat.PlayerName,
            TeamName: stat.TeamName,
            ClubLogoPath: ClubLogoService.GetClubLogoPath(stat.TeamName, _state.League?.LeagueId ?? _state.SelectedLeagueId),
            Position: stat.Position,
            PositionText: GetPositionText(stat, player),
            Goals: stat.Goals,
            Assists: stat.Assists,
            Saves: stat.Saves,
            GoalsConceded: stat.GoalsConceded,
            CleanSheets: stat.CleanSheets,
            YellowCards: stat.YellowCards,
            RedCards: stat.RedCards,
            Matches: stat.Appearances,
            Starts: stat.Starts,
            MinutesPlayed: stat.MinutesPlayed,
            AverageRating: stat.AverageRating,
            AverageRatingText: stat.AverageRating.ToString("0.00"),
            RatingBackground: GetRatingBrush(stat.AverageRating),
            FormBadgeText: formBadge.Text,
            FormBadgeBackground: formBadge.Background,
            FormBadgeForeground: formBadge.Foreground,
            IsSelectedClubPlayer: string.Equals(stat.TeamName, selectedTeamName, StringComparison.OrdinalIgnoreCase),
            RowBackground: GetRowBackground(stat.TeamName));
    }

    private Player? FindPlayer(PlayerSeasonStats stat)
    {
        var team = _state.League?.Teams
            .FirstOrDefault(team => string.Equals(team.Name, stat.TeamName, StringComparison.OrdinalIgnoreCase));

        return team?.Players
            .Concat(team.Substitutes)
            .FirstOrDefault(player => string.Equals(player.Name, stat.PlayerName, StringComparison.OrdinalIgnoreCase));
    }

    private string GetRowBackground(string teamName)
    {
        return string.Equals(_state.SelectedTeam?.Name, teamName, StringComparison.OrdinalIgnoreCase)
            ? ThemeManager.GetBrushHex("TableCurrentClubBackground", "#5A3D12")
            : ThemeManager.GetBrushHex("TableRowBackground", "#0F172A");
    }

    private static string GetPositionText(PlayerSeasonStats stat, Player? player)
    {
        var exactPosition = PositionSuitabilityService.NormalizeExactPosition(stat.ExactPosition);
        if (!string.IsNullOrWhiteSpace(exactPosition))
        {
            return exactPosition;
        }

        exactPosition = PositionSuitabilityService.NormalizeExactPosition(player?.PreferredPosition);
        if (!string.IsNullOrWhiteSpace(exactPosition))
        {
            return exactPosition;
        }

        exactPosition = PositionSuitabilityService.NormalizeExactPosition(player?.AssignedPosition);
        if (!string.IsNullOrWhiteSpace(exactPosition))
        {
            return exactPosition;
        }

        return PositionSuitabilityService.GetDefaultExactPosition(stat.Position);
    }

    private static string GetRatingBrush(double rating)
    {
        return rating switch
        {
            >= 8.0 => "#16A34A",
            >= 7.2 => "#2563EB",
            >= 6.5 => "#475569",
            _ => "#B45309"
        };
    }

    private void UpdateCategoryButtons()
    {
        foreach (var button in GetCategoryButtons())
        {
            var isActive = button.Tag is string tag &&
                Enum.TryParse<StatsCategory>(tag, out var category) &&
                category == _currentCategory;
            button.Background = ToBrush(isActive ? "#2563EB" : ThemeManager.GetBrushHex("AppSecondaryCardBackground", "#111827"));
            button.Foreground = ToBrush(isActive ? "#FFFFFF" : ThemeManager.GetBrushHex("AppTextBrush", "#E5E7EB"));
            button.BorderBrush = ToBrush(isActive ? "#2563EB" : ThemeManager.GetBrushHex("AppBorderBrush", "#243247"));
        }
    }

    private IEnumerable<Button> GetCategoryButtons()
    {
        yield return GoalsButton;
        yield return AssistsButton;
        yield return SavesButton;
        yield return CleanSheetsButton;
        yield return YellowCardsButton;
        yield return RedCardsButton;
        yield return RatingsButton;
        yield return AppearancesButton;
    }

    private void UpdateColumnVisibility()
    {
        GoalsColumn.Visibility = IsCategory(StatsCategory.Goals, StatsCategory.Ratings) ? Visibility.Visible : Visibility.Collapsed;
        AssistsColumn.Visibility = IsCategory(StatsCategory.Assists, StatsCategory.Goals, StatsCategory.Ratings) ? Visibility.Visible : Visibility.Collapsed;
        SavesColumn.Visibility = IsCategory(StatsCategory.Saves) ? Visibility.Visible : Visibility.Collapsed;
        GoalsConcededColumn.Visibility = IsCategory(StatsCategory.Saves, StatsCategory.CleanSheets) ? Visibility.Visible : Visibility.Collapsed;
        CleanSheetsColumn.Visibility = IsCategory(StatsCategory.Saves, StatsCategory.CleanSheets) ? Visibility.Visible : Visibility.Collapsed;
        YellowCardsColumn.Visibility = IsCategory(StatsCategory.YellowCards) ? Visibility.Visible : Visibility.Collapsed;
        RedCardsColumn.Visibility = IsCategory(StatsCategory.RedCards, StatsCategory.YellowCards) ? Visibility.Visible : Visibility.Collapsed;
        MatchesColumn.Visibility = IsCategory(StatsCategory.Goals, StatsCategory.Assists, StatsCategory.Ratings, StatsCategory.Appearances) ? Visibility.Visible : Visibility.Collapsed;
        StartsColumn.Visibility = IsCategory(StatsCategory.Appearances) ? Visibility.Visible : Visibility.Collapsed;
        MinutesColumn.Visibility = IsCategory(StatsCategory.Appearances) ? Visibility.Visible : Visibility.Collapsed;
        RatingColumn.Visibility = IsCategory(StatsCategory.Goals, StatsCategory.Assists, StatsCategory.Saves, StatsCategory.CleanSheets, StatsCategory.Ratings) ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool IsCategory(params StatsCategory[] categories)
    {
        return categories.Contains(_currentCategory);
    }

    private static Brush ToBrush(string color)
    {
        return (Brush)new BrushConverter().ConvertFromString(color)!;
    }

    private enum StatsCategory
    {
        Goals,
        Assists,
        Saves,
        CleanSheets,
        YellowCards,
        RedCards,
        Ratings,
        Appearances
    }

    private sealed record PlayerStatsRow(
        int Rank,
        string PlayerName,
        string TeamName,
        string ClubLogoPath,
        Position Position,
        string PositionText,
        int Goals,
        int Assists,
        int Saves,
        int GoalsConceded,
        int CleanSheets,
        int YellowCards,
        int RedCards,
        int Matches,
        int Starts,
        int MinutesPlayed,
        double AverageRating,
        string AverageRatingText,
        string RatingBackground,
        string FormBadgeText,
        string FormBadgeBackground,
        string FormBadgeForeground,
        bool IsSelectedClubPlayer,
        string RowBackground);
}
