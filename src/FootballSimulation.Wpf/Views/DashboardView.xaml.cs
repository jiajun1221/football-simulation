using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Services;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Views;

public partial class DashboardView : UserControl
{
    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;
    private readonly GameSessionService _gameSessionService = new();
    private readonly RecentResultService _recentResultService = new();
    private readonly SaveGameService _saveGameService = new();
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

    public DashboardView(GameFlowState state, Action<UserControl> navigate)
    {
        InitializeComponent();

        _state = state;
        _navigate = navigate;
        ThemeManager.ThemeChanged += ThemeManager_ThemeChanged;
        Unloaded += DashboardView_Unloaded;

        LoadDashboard();
    }

    private void ThemeManager_ThemeChanged(object? sender, EventArgs e)
    {
        LoadDashboard();
    }

    private void DashboardView_Unloaded(object sender, RoutedEventArgs e)
    {
        ThemeManager.ThemeChanged -= ThemeManager_ThemeChanged;
    }

    private void LoadDashboard()
    {
        if (_state.SelectedTeam is null || _state.League is null)
        {
            return;
        }

        SelectedTeamTextBlock.Text = _state.SelectedTeam.Name;
        SelectedClubLogoImage.Source = CreateImageSource(GetClubLogoPath(_state.SelectedTeam.Name));
        LeagueTableDataGrid.ItemsSource = CreateLeagueTableRows(_state.League, _state.SelectedTeam);
        LoadSelectedClubStats(_state.League, _state.SelectedTeam);

        var nextFixture = FindNextFixtureForTeamOrDefault(_state.League, _state.SelectedTeam);
        _state.CurrentFixture = nextFixture;
        if (nextFixture is null)
        {
            LoadNoUpcomingMatch();
        }
        else
        {
            LoadUpcomingMatch(nextFixture, _state.SelectedTeam);
        }

        UpcomingFixturesItemsControl.ItemsSource = CreateUpcomingFixtureRows(_state.League, _state.SelectedTeam);
        LoadUnavailablePlayers(_state.SelectedTeam);
    }

    private void PrepareMatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.CurrentFixture is null)
        {
            MessageBox.Show("No upcoming fixture was found.");
            return;
        }

        if (_state.League is not null && _state.SelectedTeam is not null)
        {
            if (_state.CurrentMatch is null || !IsCurrentMatchForFixture(_state.CurrentMatch, _state.CurrentFixture))
            {
                _state.CurrentMatch = _gameSessionService.CreateSelectedTeamLiveMatch(_state.League, _state.SelectedTeam);
            }
        }

        _navigate(new PreMatchView(_state, _navigate));
    }

    private void SaveGameButton_Click(object sender, RoutedEventArgs e)
    {
        SaveGame();
    }

    private void PlayerStatsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.League is null || _state.SelectedTeam is null)
        {
            return;
        }

        _navigate(new LeaguePlayerStatsView(_state, _navigate));
    }

    private void TeamSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        PrepareMatchButton_Click(sender, e);
    }

    public void SaveGame()
    {
        if (_state.League is null || _state.SelectedTeam is null)
        {
            MessageBox.Show("No active club dashboard was found.", "Save Game", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveSlotDialog(_saveGameService.GetSaveSlots())
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() != true || dialog.SelectedSlotNumber is not int slotNumber)
        {
            return;
        }

        try
        {
            var saveData = SaveGameService.CreateSaveData(_state.League, _state.SelectedTeam, _state.TransferMarket);
            _saveGameService.SaveGame(slotNumber, saveData);
            _state.CurrentSaveSlotNumber = slotNumber;
            MessageBox.Show($"Game saved to slot {slotNumber}.", "Save Game", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            MessageBox.Show(
                $"The game could not be saved.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Save Game",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private List<LeagueTableRow> CreateLeagueTableRows(League league, Team selectedTeam)
    {
        return league.Table
            .Select((entry, index) =>
            {
                var position = index + 1;
                var team = league.Teams.FirstOrDefault(candidate => candidate.Name == entry.TeamName);
                var recentResults = team is null
                    ? new List<ResultBadge>()
                    : _recentResultService.GetRecentResults(league, team)
                        .OrderBy(result => result.RoundNumber)
                        .Select(CreateResultBadge)
                        .ToList();

                return new LeagueTableRow
                {
                    Position = position,
                    Club = entry.TeamName,
                    LogoPath = GetClubLogoPath(entry.TeamName),
                    Played = entry.Played,
                    Wins = entry.Wins,
                    Draws = entry.Draws,
                    Losses = entry.Losses,
                    GoalsFor = entry.GoalsFor,
                    GoalsAgainst = entry.GoalsAgainst,
                    GoalDifference = entry.GoalDifference,
                    Points = entry.Points,
                    LastFive = recentResults,
                    IsSelectedTeam = entry.TeamName == selectedTeam.Name,
                    ZoneBrush = GetZoneBrush(position, league.Table.Count),
                    RowBackground = GetRowBackground(position)
                };
            })
            .ToList();
    }

    private void LoadUpcomingMatch(Fixture fixture, Team selectedTeam)
    {
        var isHome = fixture.HomeTeam == selectedTeam;
        var venue = GetVenueName(fixture.HomeTeam);

        UpcomingRoundTextBlock.Text = $"Round {fixture.RoundNumber}";
        UpcomingHomeNameTextBlock.Text = fixture.HomeTeam.Name;
        UpcomingAwayNameTextBlock.Text = fixture.AwayTeam.Name;
        UpcomingHomeLogoImage.Source = CreateImageSource(GetClubLogoPath(fixture.HomeTeam.Name));
        UpcomingAwayLogoImage.Source = CreateImageSource(GetClubLogoPath(fixture.AwayTeam.Name));
        VenueTextBlock.Text = $"Venue: {venue}";
        HomeAwayBadgeTextBlock.Text = isHome ? "HOME" : "AWAY";
        HomeAwayBadge.Background = new SolidColorBrush(isHome
            ? Color.FromRgb(47, 168, 79)
            : Color.FromRgb(249, 115, 22));
    }

    private void LoadNoUpcomingMatch()
    {
        UpcomingRoundTextBlock.Text = "Season complete";
        UpcomingHomeNameTextBlock.Text = "-";
        UpcomingAwayNameTextBlock.Text = "-";
        UpcomingHomeLogoImage.Source = null;
        UpcomingAwayLogoImage.Source = null;
        VenueTextBlock.Text = "No upcoming fixture was found.";
        HomeAwayBadgeTextBlock.Text = "-";
        HomeAwayBadge.Background = new SolidColorBrush(Color.FromRgb(100, 116, 139));
        PrepareMatchButton.IsEnabled = false;
    }

    private void LoadSelectedClubStats(League league, Team selectedTeam)
    {
        var tableEntry = league.Table
            .Select((entry, index) => new { Entry = entry, Position = index + 1 })
            .FirstOrDefault(item => item.Entry.TeamName == selectedTeam.Name);

        if (tableEntry is null)
        {
            PositionStatTextBlock.Text = "-";
            PointsStatTextBlock.Text = "0";
            GoalDifferenceStatTextBlock.Text = "0";
            ClubFormItemsControl.ItemsSource = null;
            return;
        }

        PositionStatTextBlock.Text = $"#{tableEntry.Position}";
        PointsStatTextBlock.Text = $"{tableEntry.Entry.Points} pts";
        GoalDifferenceStatTextBlock.Text = FormatGoalDifference(tableEntry.Entry.GoalDifference);
        ClubFormItemsControl.ItemsSource = _recentResultService.GetRecentResults(league, selectedTeam)
            .OrderBy(result => result.RoundNumber)
            .Select(CreateResultBadge)
            .ToList();
    }

    private void LoadUnavailablePlayers(Team selectedTeam)
    {
        var unavailablePlayers = selectedTeam.Players
            .Concat(selectedTeam.Substitutes)
            .Where(player => player.IsSuspended || player.IsInjured)
            .Distinct()
            .OrderByDescending(player => player.IsSuspended)
            .ThenBy(player => player.SquadNumber <= 0 ? int.MaxValue : player.SquadNumber)
            .Select(CreateUnavailablePlayerRow)
            .ToList();

        UnavailablePlayersItemsControl.ItemsSource = unavailablePlayers;
        UnavailablePlayersPanel.Visibility = unavailablePlayers.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static UnavailablePlayerRow CreateUnavailablePlayerRow(Player player)
    {
        if (player.IsSuspended)
        {
            var matchesText = player.SuspendedMatches == 1 ? "1 match" : $"{player.SuspendedMatches} matches";
            return new UnavailablePlayerRow
            {
                Icon = RedCardIcon(),
                Text = $"{player.Name} - Suspended ({matchesText})",
                BadgeText = "SUSPENDED",
                Tooltip = $"Unavailable - suspended for {matchesText}"
            };
        }

        var recoveryText = player.IsSeasonEndingInjury
            ? "season"
            : player.InjuryRecoveryMatches == 1 ? "1 match" : $"{Math.Max(1, player.InjuryRecoveryMatches)} matches";

        return new UnavailablePlayerRow
        {
            Icon = "+",
            Text = $"{player.Name} - Injured ({recoveryText})",
            BadgeText = "INJURED",
            Tooltip = $"Unavailable - injured for {recoveryText}"
        };
    }

    private static string RedCardIcon()
    {
        return char.ConvertFromUtf32(0x1F7E5);
    }

    private List<UpcomingFixtureRow> CreateUpcomingFixtureRows(League league, Team selectedTeam)
    {
        var fixtures = league.Fixtures
            .Where(fixture =>
                !fixture.IsPlayed &&
                IsTeamInFixture(fixture, selectedTeam))
            .OrderBy(fixture => fixture.RoundNumber)
            .Take(3)
            .Select(fixture =>
            {
                var isHome = fixture.HomeTeam == selectedTeam;
                var opponent = isHome ? fixture.AwayTeam : fixture.HomeTeam;
                return new UpcomingFixtureRow
                {
                    RoundText = $"R{fixture.RoundNumber}",
                    HomeAwayText = isHome ? "HOME" : "AWAY",
                    SummaryText = $"{CreateClubCode(opponent.Name)} {(isHome ? "H" : "A")} R{fixture.RoundNumber}",
                    OpponentName = CreateTwoLineText(opponent.Name),
                    VenueText = CreateTwoLineText(GetVenueName(fixture.HomeTeam)),
                    OpponentLogoPath = GetClubLogoPath(opponent.Name),
                    HomeAwayBrush = GetHomeAwayBadgeBackground(isHome),
                    HomeAwayForeground = GetHomeAwayBadgeForeground(isHome)
                };
            })
            .ToList();

        return fixtures.Count == 0
            ? [new UpcomingFixtureRow
            {
                OpponentName = "No upcoming fixtures",
                SummaryText = "No fixtures",
                VenueText = "Season schedule is complete.",
                RoundText = "-",
                HomeAwayText = "-",
                HomeAwayBrush = ThemeManager.GetBrushHex("AppSecondaryCardBackground", "#111827"),
                HomeAwayForeground = ThemeManager.GetBrushHex("AppMutedTextBrush", "#94A3B8")
            }]
            : fixtures;
    }

    private static string GetHomeAwayBadgeBackground(bool isHome)
    {
        return isHome ? "#2FA84F" : "#F97316";
    }

    private static string GetHomeAwayBadgeForeground(bool isHome)
    {
        return "#FFFFFF";
    }

    private static string CreateClubCode(string clubName)
    {
        var commonCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Arsenal"] = "ARS",
            ["Aston Villa"] = "AVL",
            ["Chelsea"] = "CHE",
            ["Liverpool"] = "LIV",
            ["Manchester City"] = "MCI",
            ["Manchester United"] = "MUN",
            ["Newcastle United"] = "NEW",
            ["Nottingham Forest"] = "NFO",
            ["Tottenham Hotspur"] = "TOT",
            ["West Ham United"] = "WHU",
            ["Wolverhampton Wanderers"] = "WOL"
        };

        if (commonCodes.TryGetValue(clubName, out var code))
        {
            return code;
        }

        var words = clubName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !string.Equals(word, "FC", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var letters = words.Length >= 2
            ? string.Concat(words.Select(word => word[0]))
            : clubName;

        return new string(letters
            .Where(char.IsLetterOrDigit)
            .Take(3)
            .Select(char.ToUpperInvariant)
            .ToArray());
    }

    private static bool IsTeamInFixture(Fixture fixture, Team team)
    {
        return fixture.HomeTeam == team || fixture.AwayTeam == team;
    }

    private static Fixture? FindNextFixtureForTeamOrDefault(League league, Team selectedTeam)
    {
        return league.Fixtures
            .Where(fixture => !fixture.IsPlayed && IsTeamInFixture(fixture, selectedTeam))
            .OrderBy(fixture => fixture.RoundNumber)
            .FirstOrDefault();
    }

    private static bool IsCurrentMatchForFixture(Match match, Fixture? fixture)
    {
        return fixture is not null &&
            match.HomeTeam == fixture.HomeTeam &&
            match.AwayTeam == fixture.AwayTeam;
    }

    private static string GetVenueName(Team homeTeam)
    {
        return TeamVenueService.GetDisplayVenue(homeTeam);
    }

    private static string CreateTwoLineText(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 2)
        {
            return text;
        }

        var bestSplitIndex = 1;
        var bestLineBalance = int.MaxValue;
        for (var splitIndex = 1; splitIndex < words.Length; splitIndex++)
        {
            var firstLineLength = string.Join(" ", words.Take(splitIndex)).Length;
            var secondLineLength = string.Join(" ", words.Skip(splitIndex)).Length;
            var lineBalance = Math.Abs(firstLineLength - secondLineLength);
            if (lineBalance < bestLineBalance)
            {
                bestSplitIndex = splitIndex;
                bestLineBalance = lineBalance;
            }
        }

        return $"{string.Join(" ", words.Take(bestSplitIndex))}{Environment.NewLine}{string.Join(" ", words.Skip(bestSplitIndex))}";
    }

    private static string FormatGoalDifference(int goalDifference)
    {
        return goalDifference > 0 ? $"+{goalDifference}" : goalDifference.ToString();
    }

    private static ResultBadge CreateResultBadge(RecentMatchResult result)
    {
        return new ResultBadge
        {
            ResultType = result.ResultType,
            BadgeBrush = result.ResultType switch
            {
                "W" => "#2FA84F",
                "L" => "#D94343",
                _ => "#9AA3AF"
            }
        };
    }

    private static string GetZoneBrush(int position, int tableSize)
    {
        if (position <= 4)
        {
            return "#3B82F6";
        }

        if (position == 5)
        {
            return "#F97316";
        }

        if (position == 6)
        {
            return "#22C55E";
        }

        return position > tableSize - 3 ? "#EF4444" : "Transparent";
    }

    private static string GetRowBackground(int position)
    {
        return position % 2 == 0
            ? ThemeManager.GetBrushHex("TableAlternateRowBackground", "#132033")
            : ThemeManager.GetBrushHex("TableRowBackground", "#0F172A");
    }

    private static ImageSource? CreateImageSource(string logoPath)
    {
        if (string.IsNullOrWhiteSpace(logoPath))
        {
            return null;
        }

        try
        {
            return new BitmapImage(new Uri(logoPath, UriKind.Absolute));
        }
        catch
        {
            return null;
        }
    }

    private string GetClubLogoPath(string clubName)
    {
        return ClubLogoService.GetClubLogoPath(clubName, _state.League?.LeagueId ?? _state.SelectedLeagueId);
    }

    private static IEnumerable<string> GetLogoCandidatePaths(string clubName)
    {
        yield return TeamSelectionView.GetClubLogoPath(clubName);

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

    private sealed class LeagueTableRow
    {
        public int Position { get; init; }
        public string Club { get; init; } = string.Empty;
        public string LogoPath { get; init; } = string.Empty;
        public int Played { get; init; }
        public int Wins { get; init; }
        public int Draws { get; init; }
        public int Losses { get; init; }
        public int GoalsFor { get; init; }
        public int GoalsAgainst { get; init; }
        public int GoalDifference { get; init; }
        public int Points { get; init; }
        public List<ResultBadge> LastFive { get; init; } = [];
        public bool IsSelectedTeam { get; init; }
        public string ZoneBrush { get; init; } = "Transparent";
        public string RowBackground { get; init; } = "#FFFFFF";
    }

    private sealed class ResultBadge
    {
        public string ResultType { get; init; } = string.Empty;
        public string BadgeBrush { get; init; } = "#9AA3AF";
    }

    private sealed class UnavailablePlayerRow
    {
        public string Icon { get; init; } = string.Empty;
        public string Text { get; init; } = string.Empty;
        public string BadgeText { get; init; } = string.Empty;
        public string Tooltip { get; init; } = string.Empty;
    }

    private sealed class UpcomingFixtureRow
    {
        public string RoundText { get; init; } = string.Empty;
        public string HomeAwayText { get; init; } = string.Empty;
        public string SummaryText { get; init; } = string.Empty;
        public string OpponentName { get; init; } = string.Empty;
        public string VenueText { get; init; } = string.Empty;
        public string OpponentLogoPath { get; init; } = string.Empty;
        public string HomeAwayBrush { get; init; } = "#E1E5EA";
        public string HomeAwayForeground { get; init; } = "#64748B";
    }
}
