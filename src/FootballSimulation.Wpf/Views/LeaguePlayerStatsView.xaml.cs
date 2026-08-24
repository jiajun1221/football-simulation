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
    private readonly TransferMarketService _transferMarketService = new();
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
        MatchHistoryContentControl.Content = new MyTeamResultsView(_state, _navigate, showHeader: false);
        LoadOtherClubs();
        SelectCategory(StatsCategory.Goals);
    }

    private void LoadOtherClubs()
    {
        if (_state.League is null)
        {
            return;
        }

        var clubs = _state.League.Teams
            .Where(team => _state.SelectedTeam is null ||
                !team.Name.Equals(_state.SelectedTeam.Name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(team => team.Name)
            .ToList();

        OtherClubFormationPanel.IsReadOnlyMode = true;
        OtherClubComboBox.ItemsSource = clubs.Select(team => new ComboBoxItem
        {
            Content = team.Name,
            Tag = team
        }).ToList();
        OtherClubComboBox.SelectedIndex = clubs.Count > 0 ? 0 : -1;
    }

    private void OtherClubComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OtherClubComboBox.SelectedItem is ComboBoxItem { Tag: Team team })
        {
            ShowOtherClub(team);
        }
    }

    private void ShowOtherClub(Team team)
    {
        OtherClubFormationPanel.LoadTeam(team);
        var firstPlayer = team.Players
            .Concat(team.Substitutes)
            .OrderByDescending(player => player.IsStarter || player.IsOnPitch)
            .ThenByDescending(player => player.OverallRating)
            .FirstOrDefault();
        if (firstPlayer is not null)
        {
            ShowOtherClubPlayer(firstPlayer);
        }
        else
        {
            OtherClubPlayerDetailPanel.ShowEmpty();
        }
        OtherClubNameTextBlock.Text = team.Name;
        OtherClubVenueTextBlock.Text = string.IsNullOrWhiteSpace(team.StadiumName) ? team.Venue : team.StadiumName;
        OtherClubLogoImage.Source = ClubLogoService.LoadClubLogo(team.Name, _state.League?.LeagueId ?? string.Empty);
        OtherClubFormationTextBlock.Text = string.IsNullOrWhiteSpace(team.Formation) ? "4-3-3" : team.Formation;
        OtherClubMentalityTextBlock.Text = $"Mentality: {team.Tactics.Mentality}";
        OtherClubTempoTextBlock.Text = $"Tempo: {FormatTacticalLevel(team.Tactics.Tempo)}";
        OtherClubWidthTextBlock.Text = $"Width: {FormatTacticalLevel(team.Tactics.Width)}";
        OtherClubPressingTextBlock.Text = $"Pressing: {FormatTacticalLevel(team.Tactics.PressingIntensity)}";

        var squad = team.Players
            .Concat(team.Substitutes)
            .GroupBy(player => string.IsNullOrWhiteSpace(player.PlayerId) ? player.Name : player.PlayerId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var starters = squad.Where(player => player.IsStarter).ToList();
        if (starters.Count == 0)
        {
            starters = squad
                .OrderBy(GetPositionOrder)
                .ThenByDescending(player => player.OverallRating)
                .Take(11)
                .ToList();
        }

        OtherClubStartingListBox.ItemsSource = starters
            .OrderBy(GetPositionOrder)
            .ThenByDescending(player => player.OverallRating)
            .Select(player => new OtherClubStarterRow(GetPlayerPosition(player), player.Name, player.OverallRating))
            .ToList();
        OtherClubSquadDataGrid.ItemsSource = squad
            .OrderByDescending(player => player.IsStarter)
            .ThenBy(GetPositionOrder)
            .ThenByDescending(player => player.OverallRating)
            .Select(player => new OtherClubSquadRow(
                player.SquadNumber <= 0 ? "-" : player.SquadNumber.ToString(),
                player.Name,
                GetPlayerPosition(player),
                player.Age?.ToString() ?? "-",
                player.OverallRating,
                PlayerContractService.FormatRole(player.Role),
                GetPlayerAvailability(player)))
            .ToList();
    }

    private void OtherClubFormationPanel_PlayerSelected(object? sender, Player player)
    {
        ShowOtherClubPlayer(player);
    }

    private void ShowOtherClubPlayer(Player player)
    {
        if (_state.TransferMarket is null || _state.League is null)
        {
            OtherClubPlayerDetailPanel.ShowEmpty();
            return;
        }

        var listing = _transferMarketService
            .GetAllPlayerListings(_state.TransferMarket, _state.League.PlayerStats)
            .FirstOrDefault(item => item.Player.PlayerId.Equals(player.PlayerId, StringComparison.OrdinalIgnoreCase));
        if (listing is null)
        {
            OtherClubPlayerDetailPanel.ShowEmpty();
            return;
        }

        var stat = _state.League.PlayerStats.FirstOrDefault(item =>
            (!string.IsNullOrWhiteSpace(item.PlayerId) && item.PlayerId.Equals(player.PlayerId, StringComparison.OrdinalIgnoreCase)) ||
            item.PlayerName.Equals(player.Name, StringComparison.OrdinalIgnoreCase));
        var statusText = player.IsInjured ? "Injured" : player.IsSuspended ? "Suspended" : "Available";
        var statusBrush = player.IsInjured || player.IsSuspended ? "#EF4444" : "#10B981";

        OtherClubPlayerDetailPanel.ShowPlayer(new TransferPlayerDetailContext(
            listing,
            TransferDetailMode.Squad,
            stat,
            IsOwnPlayer: false,
            IsTransferWindowOpen: false,
            TransferWindowTooltip: null,
            IsShortlisted: false,
            IsListedForSale: player.IsListedForSale,
            CanToggleShortlist: false,
            statusText,
            statusBrush,
            statusText));
    }

    private static string FormatTacticalLevel(int value) => value switch
    {
        <= 30 => "Low",
        <= 45 => "Cautious",
        <= 60 => "Balanced",
        <= 75 => "High",
        _ => "Very High"
    };

    private static int GetPositionOrder(Player player) => player.Position switch
    {
        Position.Goalkeeper => 0,
        Position.Defender => 1,
        Position.Midfielder => 2,
        Position.Forward => 3,
        _ => 4
    };

    private static string GetPlayerPosition(Player player) =>
        !string.IsNullOrWhiteSpace(player.AssignedPosition) ? player.AssignedPosition :
        !string.IsNullOrWhiteSpace(player.PreferredPosition) ? player.PreferredPosition :
        player.Position.ToString();

    private static string GetPlayerAvailability(Player player)
    {
        if (player.IsInjured) return "Injured";
        if (player.SuspendedMatches > 0) return "Suspended";
        if (player.TransferStatus == PlayerTransferStatus.Listed) return "Listed";
        return player.IsStarter ? "Starting XI" : "Available";
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
            button.Background = ToBrush(isActive ? "#000000" : "#EAF0F7");
            button.Foreground = ToBrush(isActive ? "#FFFFFF" : ThemeManager.GetBrushHex("AppTextBrush", "#E5E7EB"));
            button.BorderBrush = ToBrush("Transparent");
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

    private sealed record OtherClubStarterRow(string Position, string Name, int Overall);

    private sealed record OtherClubSquadRow(
        string Number,
        string Name,
        string Position,
        string Age,
        int Overall,
        string Role,
        string Status);
}
