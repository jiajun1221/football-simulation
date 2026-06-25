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
    private readonly TransferMarketService _transferMarketService = new();
    private readonly YouthAcademyService _youthAcademyService = new();
    private readonly YouthScoutService _youthScoutService = new();
    private readonly SeasonCompletionService _seasonCompletionService = new();
    private CompetitionType? _fixtureFilter;
    private DashboardTableView _activeTableView = DashboardTableView.League;
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
        SeasonTextBlock.Text = $"Season {FormatSeasonLabel(_state.League.Season)}";
        EnsureFixtureFilterOptions();
        SelectedClubLogoImage.Source = CreateImageSource(GetClubLogoPath(_state.SelectedTeam.Name));
        RefreshSelectedTableView();
        LoadSelectedClubStats(_state.League, _state.SelectedTeam);

        var seasonComplete = IsSeasonCompleted(_state.League);
        if (seasonComplete)
        {
            _state.League.IsCompleted = true;
        }

        var nextFixture = seasonComplete
            ? null
            : FindNextFixtureForTeamOrDefault(_state.League, _state.SelectedTeam);
        _state.CurrentFixture = nextFixture;
        if (nextFixture is null)
        {
            LoadNoUpcomingMatch(seasonComplete);
        }
        else
        {
            LoadUpcomingMatch(nextFixture, _state.SelectedTeam);
        }

        UpcomingFixturesItemsControl.ItemsSource = seasonComplete
            ? []
            : CreateUpcomingFixtureRows(_state.League, _state.SelectedTeam);
        if (!seasonComplete)
        {
            RunTransferMarketRoundProcessing();
        }

        UpdateDashboardNotifications();
    }

    private void EnsureFixtureFilterOptions()
    {
        if (FixtureFilterComboBox.ItemsSource is not null)
        {
            return;
        }

        var options = new List<FixtureFilterOption>
        {
            new("All Fixtures", null),
            new("Premier League", CompetitionType.PremierLeague),
            new("FA Cup", CompetitionType.FACup),
            new("League Cup", CompetitionType.LeagueCup),
            new("Champions League", CompetitionType.ChampionsLeague)
        };
        FixtureFilterComboBox.DisplayMemberPath = nameof(FixtureFilterOption.Label);
        FixtureFilterComboBox.SelectedValuePath = nameof(FixtureFilterOption.Competition);
        TextSearch.SetTextPath(FixtureFilterComboBox, nameof(FixtureFilterOption.Label));
        FixtureFilterComboBox.ItemsSource = options;
        FixtureFilterComboBox.SelectedIndex = 0;
    }

    private void FixtureFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FixtureFilterComboBox.SelectedItem is FixtureFilterOption option)
        {
            _fixtureFilter = option.Competition;
        }

        if (_state.League is not null && _state.SelectedTeam is not null)
        {
            UpcomingFixturesItemsControl.ItemsSource = IsSeasonCompleted(_state.League)
                ? []
                : CreateUpcomingFixtureRows(_state.League, _state.SelectedTeam);
        }
    }

    private void LeagueTableTabButton_Click(object sender, RoutedEventArgs e)
    {
        _activeTableView = DashboardTableView.League;
        RefreshSelectedTableView();
    }

    private void ChampionsLeagueTableTabButton_Click(object sender, RoutedEventArgs e)
    {
        _activeTableView = DashboardTableView.ChampionsLeague;
        RefreshSelectedTableView();
    }

    private void RefreshSelectedTableView()
    {
        if (_state.League is null || _state.SelectedTeam is null)
        {
            return;
        }

        var hasChampionsLeagueContent = HasChampionsLeagueTable(_state.League) ||
            HasChampionsLeagueKnockoutBracket(_state.League);
        ChampionsLeagueTableTabButton.IsEnabled = hasChampionsLeagueContent;
        if (_activeTableView == DashboardTableView.ChampionsLeague && !hasChampionsLeagueContent)
        {
            _activeTableView = DashboardTableView.League;
        }

        var showingChampionsLeague = _activeTableView == DashboardTableView.ChampionsLeague;
        var showingChampionsLeagueBracket = showingChampionsLeague && HasChampionsLeagueKnockoutBracket(_state.League);
        TableTitleTextBlock.Text = showingChampionsLeagueBracket
            ? "Champions League Bracket"
            : showingChampionsLeague ? "Champions League Table" : "League Table";
        LeagueTableLegendPanel.Visibility = showingChampionsLeague ? Visibility.Collapsed : Visibility.Visible;
        ChampionsLeagueLegendPanel.Visibility = showingChampionsLeague && !showingChampionsLeagueBracket
            ? Visibility.Visible
            : Visibility.Collapsed;
        ChampionsLeagueBracketLegendPanel.Visibility = showingChampionsLeagueBracket
            ? Visibility.Visible
            : Visibility.Collapsed;
        LastFiveLegendPanel.Visibility = showingChampionsLeagueBracket ? Visibility.Collapsed : Visibility.Visible;
        LastFiveColumn.Visibility = showingChampionsLeagueBracket ? Visibility.Collapsed : Visibility.Visible;
        LeagueTableDataGrid.Visibility = showingChampionsLeagueBracket ? Visibility.Collapsed : Visibility.Visible;
        ChampionsLeagueBracketScrollViewer.Visibility = showingChampionsLeagueBracket ? Visibility.Visible : Visibility.Collapsed;
        ChampionsLeagueBracketItemsControl.ItemsSource = showingChampionsLeagueBracket
            ? CreateChampionsLeagueBracketRoundGroups(_state.League, _state.SelectedTeam)
            : null;
        LeagueTableDataGrid.ItemsSource = showingChampionsLeagueBracket
            ? null
            : showingChampionsLeague
                ? CreateChampionsLeagueTableRows(_state.League, _state.SelectedTeam)
                : CreateLeagueTableRows(_state.League, _state.SelectedTeam);
        ApplyTableTabVisuals();
    }

    private void ApplyTableTabVisuals()
    {
        ApplyTableTabVisual(LeagueTableTabButton, _activeTableView == DashboardTableView.League);
        ApplyTableTabVisual(ChampionsLeagueTableTabButton, _activeTableView == DashboardTableView.ChampionsLeague);
    }

    private static void ApplyTableTabVisual(Button button, bool isActive)
    {
        button.Background = CreateBrush(isActive ? "#030712" : "#FFFFFF");
        button.Foreground = CreateBrush(isActive ? "#FFFFFF" : "#061226");
        button.BorderBrush = CreateBrush(isActive ? "#030712" : "#CBD5E1");
        button.FontWeight = isActive ? FontWeights.Black : FontWeights.Bold;
    }

    private void RunTransferMarketRoundProcessing()
    {
        if (_state.League is null || _state.SelectedTeam is null)
        {
            return;
        }

        _state.TransferMarket ??= _transferMarketService.CreateInitialState(_state.League);
        _transferMarketService.BindActiveLeague(_state.TransferMarket, _state.League);
        _transferMarketService.RunAiTransferActivity(
            _state.TransferMarket,
            _state.League,
            _state.SelectedTeam,
            GetCurrentRoundForTransferProcessing());
        _youthAcademyService.EnsureAcademies(_state.League);
        _youthScoutService.RunAiScoutingActivity(
            _state.League,
            _state.TransferMarket,
            _state.SelectedTeam,
            GetCurrentRoundForTransferProcessing());
    }

    private void UpdateDashboardNotifications()
    {
        var pendingTransferOfferCount = GetPendingIncomingTransferOfferCount();
        TransferMarketNotificationCountTextBlock.Text = pendingTransferOfferCount.ToString();
        TransferMarketNotificationBadge.Visibility = pendingTransferOfferCount > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        YouthAcademyNotificationBadge.Visibility = HasCompletedScoutReportNotification()
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private int GetPendingIncomingTransferOfferCount()
    {
        if (_state.TransferMarket is null || _state.SelectedTeam is null)
        {
            return 0;
        }

        return _state.TransferMarket.Offers.Count(offer => IsPendingIncomingOffer(offer, _state.SelectedTeam));
    }

    private static bool IsPendingIncomingOffer(TransferOffer offer, Team selectedTeam)
    {
        return !offer.IsUserOffer &&
            offer.FromClubName.Equals(selectedTeam.Name, StringComparison.OrdinalIgnoreCase) &&
            offer.Status is OfferStatus.Pending or OfferStatus.PendingUntilWindowOpens or OfferStatus.Countered;
    }

    private bool HasCompletedScoutReportNotification()
    {
        if (_state.League is null || _state.SelectedTeam is null)
        {
            return false;
        }

        _youthAcademyService.EnsureAcademies(_state.League);
        var academy = _youthAcademyService.GetAcademy(_state.League, _state.SelectedTeam.Name);
        _youthScoutService.EnsureScoutNetwork(academy);
        return academy.ScoutAssignments.Any(assignment =>
            assignment.ProgressMatches >= assignment.RequiredMatches &&
            !string.IsNullOrWhiteSpace(assignment.ActiveReportId));
    }

    private int GetCurrentRoundForTransferProcessing()
    {
        if (_state.League is null || _state.SelectedTeam is null)
        {
            return 1;
        }

        return _state.League.Fixtures
            .Where(fixture => !fixture.IsPlayed && IsTeamInFixture(fixture, _state.SelectedTeam))
            .OrderBy(fixture => fixture.RoundNumber)
            .Select(fixture => fixture.RoundNumber)
            .FirstOrDefault(_state.League.Fixtures.Count == 0 ? 1 : _state.League.Fixtures.Max(fixture => fixture.RoundNumber));
    }

    private void PrepareMatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsSeasonCompleted(_state.League))
        {
            if (_state.League is not null)
            {
                _state.League.IsCompleted = true;
            }

            OpenSeasonResultFlow();
            return;
        }

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

    private void OpenSeasonResultFlow()
    {
        if (_state.League is null || _state.SelectedTeam is null)
        {
            MessageBox.Show("No completed season was found.");
            return;
        }

        TrophyCelebrationService.EnqueueSeasonResultCelebration(_state);
        try
        {
            _navigate(_state.TrophyCelebrationQueue.Count > 0
                ? new ChampionCelebrationView(_state, _navigate, () => new EndSeasonResultView(_state, _navigate))
                : new EndSeasonResultView(_state, _navigate));
        }
        catch (Exception ex)
        {
            _state.TrophyCelebrationQueue.Clear();
            MessageBox.Show(
                $"The trophy celebration could not be opened, so the Season Overview will be shown instead.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Season Result",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            _navigate(new EndSeasonResultView(_state, _navigate));
        }
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

    private void MatchResultsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.League is null || _state.SelectedTeam is null)
        {
            return;
        }

        _navigate(new MyTeamResultsView(_state, _navigate));
    }

    private void MySquadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.SelectedTeam is null)
        {
            return;
        }

        _navigate(new SquadOverviewView(_state, _navigate));
    }

    private void TransferMarketButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.League is null || _state.SelectedTeam is null)
        {
            return;
        }

        _state.TransferMarket ??= _transferMarketService.CreateInitialState(_state.League);
        _transferMarketService.BindActiveLeague(_state.TransferMarket, _state.League);
        _navigate(new TransferMarketView(_state, _navigate));
    }

    private void YouthAcademyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.League is null || _state.SelectedTeam is null)
        {
            return;
        }

        _state.TransferMarket ??= _transferMarketService.CreateInitialState(_state.League);
        _transferMarketService.BindActiveLeague(_state.TransferMarket, _state.League);
        _youthAcademyService.EnsureAcademies(_state.League);
        _navigate(new YouthAcademyView(_state, _navigate));
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
                    : _recentResultService.GetRecentResults(league, team, competition: CompetitionType.PremierLeague)
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

    private List<LeagueTableRow> CreateChampionsLeagueTableRows(League league, Team selectedTeam)
    {
        var state = GetChampionsLeagueState(league);
        if (state is null)
        {
            return [];
        }

        var sortedStandings = state.Standings
            .OrderByDescending(row => row.Points)
            .ThenByDescending(row => row.GoalDifference)
            .ThenByDescending(row => row.GoalsFor)
            .ThenBy(row => row.TeamName)
            .ToList();
        return sortedStandings
            .Select((entry, index) =>
            {
                var position = index + 1;
                var recentResults = CreateChampionsLeagueRecentResults(league, entry.TeamName);
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
                    IsSelectedTeam = entry.TeamName.Equals(selectedTeam.Name, StringComparison.OrdinalIgnoreCase),
                    ZoneBrush = GetChampionsLeagueZoneBrush(position),
                    RowBackground = GetRowBackground(position)
                };
            })
            .ToList();
    }

    private List<BracketRoundGroup> CreateChampionsLeagueBracketRoundGroups(League league, Team selectedTeam)
    {
        var roundOrder = GetCompetitionRoundOrder(league, CompetitionType.ChampionsLeague);
        return league.Fixtures
            .Where(fixture => fixture.Competition == CompetitionType.ChampionsLeague && fixture.IsKnockout)
            .Where(fixture => !fixture.KnockoutRoundKey.Equals("League Phase", StringComparison.OrdinalIgnoreCase))
            .GroupBy(GetBracketRoundName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var fixtures = group
                    .OrderBy(GetFixtureSortRound)
                    .ThenBy(fixture => fixture.RoundNumber)
                    .ThenBy(fixture => fixture.HomeTeam.Name)
                    .Select(fixture => CreateBracketMatchRow(fixture, selectedTeam))
                    .ToList();

                return new BracketRoundGroup
                {
                    RoundName = group.Key,
                    SummaryText = CreateBracketRoundSummary(fixtures),
                    SortOrder = GetRoundSortOrder(roundOrder, group.Key, group.Min(GetFixtureSortRound)),
                    Matches = fixtures
                };
            })
            .OrderBy(group => group.SortOrder)
            .ThenBy(group => group.RoundName)
            .ToList();
    }

    private BracketMatchRow CreateBracketMatchRow(Fixture fixture, Team selectedTeam)
    {
        var winner = fixture.WinningTeamName;
        var homeWon = !string.IsNullOrWhiteSpace(winner) &&
            winner.Equals(fixture.HomeTeam.Name, StringComparison.OrdinalIgnoreCase);
        var awayWon = !string.IsNullOrWhiteSpace(winner) &&
            winner.Equals(fixture.AwayTeam.Name, StringComparison.OrdinalIgnoreCase);
        var isSelectedTeamMatch =
            fixture.HomeTeam.Name.Equals(selectedTeam.Name, StringComparison.OrdinalIgnoreCase) ||
            fixture.AwayTeam.Name.Equals(selectedTeam.Name, StringComparison.OrdinalIgnoreCase);

        return new BracketMatchRow
        {
            HomeTeamName = fixture.HomeTeam.Name,
            AwayTeamName = fixture.AwayTeam.Name,
            HomeLogoPath = GetClubLogoPath(fixture.HomeTeam.Name),
            AwayLogoPath = GetClubLogoPath(fixture.AwayTeam.Name),
            HomeSeedText = homeWon ? "Advanced" : "Home",
            AwaySeedText = awayWon ? "Advanced" : "Away",
            ScoreText = CreateBracketScoreText(fixture),
            HomeFontWeight = homeWon ? "Black" : "SemiBold",
            AwayFontWeight = awayWon ? "Black" : "SemiBold",
            RowBackground = isSelectedTeamMatch
                ? ThemeManager.GetBrushHex("TableCurrentClubBackground", "#FEF3C7")
                : ThemeManager.GetBrushHex("AppSecondaryCardBackground", "#F8FAFC"),
            BorderBrush = isSelectedTeamMatch
                ? ThemeManager.GetBrushHex("AppHighlightBrush", "#FACC15")
                : ThemeManager.GetBrushHex("AppBorderBrush", "#D8E0EA"),
            BorderThickness = isSelectedTeamMatch ? new Thickness(2) : new Thickness(1)
        };
    }

    private static string CreateBracketRoundSummary(IReadOnlyCollection<BracketMatchRow> matches)
    {
        var completed = matches.Count(match => match.ScoreText != "vs");
        return completed == matches.Count
            ? $"{matches.Count} matches completed"
            : $"{completed}/{matches.Count} matches completed";
    }

    private static string CreateBracketScoreText(Fixture fixture)
    {
        if (fixture.Result is null)
        {
            return "vs";
        }

        var score = $"{fixture.Result.HomeScore}-{fixture.Result.AwayScore}";
        if (fixture.PenaltyHomeScore.HasValue && fixture.PenaltyAwayScore.HasValue)
        {
            return $"{score}\n{fixture.PenaltyHomeScore}-{fixture.PenaltyAwayScore} pens";
        }

        if (fixture.ExtraTimeHomeScore.HasValue && fixture.ExtraTimeAwayScore.HasValue &&
            (fixture.ExtraTimeHomeScore != fixture.Result.HomeScore || fixture.ExtraTimeAwayScore != fixture.Result.AwayScore))
        {
            return $"{fixture.ExtraTimeHomeScore}-{fixture.ExtraTimeAwayScore}\nAET";
        }

        return score;
    }

    private static List<string> GetCompetitionRoundOrder(League league, CompetitionType competition)
    {
        return league.CompetitionStates
            .FirstOrDefault(state => state.Competition == competition)
            ?.RoundOrder ?? [];
    }

    private static int GetRoundSortOrder(IReadOnlyList<string> roundOrder, string roundName, int fallbackCalendarRound)
    {
        var orderIndex = roundOrder
            .Select((name, index) => new { name, index })
            .FirstOrDefault(item => item.name.Equals(roundName, StringComparison.OrdinalIgnoreCase))
            ?.index;
        return orderIndex.HasValue ? orderIndex.Value * 1000 : 10_000 + fallbackCalendarRound;
    }

    private static string GetBracketRoundName(Fixture fixture)
    {
        if (!string.IsNullOrWhiteSpace(fixture.KnockoutRoundKey) &&
            !fixture.KnockoutRoundKey.Equals("League Phase", StringComparison.OrdinalIgnoreCase))
        {
            return fixture.KnockoutRoundKey;
        }

        return GetFixtureRoundText(fixture);
    }

    private static List<ResultBadge> CreateChampionsLeagueRecentResults(League league, string teamName)
    {
        return league.Fixtures
            .Where(fixture =>
                fixture.Competition == CompetitionType.ChampionsLeague &&
                fixture.IsPlayed &&
                fixture.Result is not null &&
                IsTeamInFixture(fixture, teamName))
            .OrderByDescending(fixture => GetFixtureSortRound(fixture))
            .Take(5)
            .OrderBy(fixture => GetFixtureSortRound(fixture))
            .Select(fixture => CreateResultBadge(CreateRecentResult(fixture, teamName)))
            .ToList();
    }

    private static RecentMatchResult CreateRecentResult(Fixture fixture, string teamName)
    {
        var match = fixture.Result!;
        var isHome = fixture.HomeTeam.Name.Equals(teamName, StringComparison.OrdinalIgnoreCase);
        var selectedGoals = isHome ? match.HomeScore : match.AwayScore;
        var opponentGoals = isHome ? match.AwayScore : match.HomeScore;
        var opponent = isHome ? fixture.AwayTeam : fixture.HomeTeam;

        return new RecentMatchResult
        {
            RoundNumber = fixture.RoundNumber,
            OpponentName = opponent.Name,
            ScoreText = $"{selectedGoals} - {opponentGoals}",
            ResultType = selectedGoals > opponentGoals ? "W" : selectedGoals < opponentGoals ? "L" : "D"
        };
    }

    private static bool IsTeamInFixture(Fixture fixture, string teamName)
    {
        return fixture.HomeTeam.Name.Equals(teamName, StringComparison.OrdinalIgnoreCase) ||
            fixture.AwayTeam.Name.Equals(teamName, StringComparison.OrdinalIgnoreCase);
    }

    private static int GetFixtureSortRound(Fixture fixture)
    {
        return fixture.CalendarRound > 0 ? fixture.CalendarRound : fixture.RoundNumber;
    }

    private static SeasonCompetitionState? GetChampionsLeagueState(League league)
    {
        return league.CompetitionStates.FirstOrDefault(state => state.Competition == CompetitionType.ChampionsLeague);
    }

    private static bool HasChampionsLeagueTable(League league)
    {
        return GetChampionsLeagueState(league)?.Standings.Count > 0;
    }

    private static bool HasChampionsLeagueKnockoutBracket(League league)
    {
        return league.Fixtures.Any(fixture =>
            fixture.Competition == CompetitionType.ChampionsLeague &&
            fixture.IsKnockout &&
            !fixture.KnockoutRoundKey.Equals("League Phase", StringComparison.OrdinalIgnoreCase));
    }

    private void LoadUpcomingMatch(Fixture fixture, Team selectedTeam)
    {
        var isHome = fixture.HomeTeam == selectedTeam;
        var venue = GetVenueName(fixture.HomeTeam);

        UpcomingMatchTitleTextBlock.Text = "Upcoming Match";
        UpcomingRoundTextBlock.Text = $"{CompetitionDisplayService.GetShortName(fixture.Competition)}{Environment.NewLine}{GetFixtureRoundText(fixture)}";
        UpcomingHomeNameTextBlock.Text = fixture.HomeTeam.Name;
        UpcomingAwayNameTextBlock.Text = fixture.AwayTeam.Name;
        UpcomingHomeLogoImage.Source = CreateImageSource(GetClubLogoPath(fixture.HomeTeam.Name));
        UpcomingAwayLogoImage.Source = CreateImageSource(GetClubLogoPath(fixture.AwayTeam.Name));
        VenueTextBlock.Text = $"{CompetitionDisplayService.GetName(fixture.Competition)} - {GetFixtureRoundText(fixture)} - Venue: {venue}";
        HomeAwayBadgeTextBlock.Text = $"{CompetitionDisplayService.GetShortName(fixture.Competition)} - {(isHome ? "HOME" : "AWAY")}";
        HomeAwayBadge.Background = CreateBrush(CompetitionDisplayService.GetColor(fixture.Competition));
        PrepareMatchButton.Content = "Prepare Match";
        PrepareMatchButton.IsEnabled = true;
    }

    private void LoadNoUpcomingMatch(bool seasonComplete)
    {
        UpcomingMatchTitleTextBlock.Text = seasonComplete ? "Season Finished" : "Upcoming Match";
        UpcomingRoundTextBlock.Text = seasonComplete ? "Final Table" : "No fixture";
        UpcomingHomeNameTextBlock.Text = seasonComplete ? _state.SelectedTeam?.Name ?? "-" : "-";
        UpcomingAwayNameTextBlock.Text = seasonComplete ? "Season Finished" : "-";
        UpcomingHomeLogoImage.Source = null;
        UpcomingAwayLogoImage.Source = null;
        if (seasonComplete && _state.SelectedTeam is not null)
        {
            UpcomingHomeLogoImage.Source = CreateImageSource(GetClubLogoPath(_state.SelectedTeam.Name));
        }

        VenueTextBlock.Text = seasonComplete
            ? "All league fixtures have been played. View the season results."
            : "No upcoming fixture was found.";
        HomeAwayBadgeTextBlock.Text = seasonComplete ? "SEASON COMPLETE" : "-";
        HomeAwayBadge.Background = seasonComplete
            ? new SolidColorBrush(Color.FromRgb(183, 121, 31))
            : new SolidColorBrush(Color.FromRgb(100, 116, 139));
        PrepareMatchButton.Content = seasonComplete
            ? "Season Result"
            : "Prepare Match";
        PrepareMatchButton.IsEnabled = seasonComplete;
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
        ClubFormItemsControl.ItemsSource = _recentResultService.GetRecentResults(league, selectedTeam, competition: CompetitionType.PremierLeague)
            .OrderBy(result => result.RoundNumber)
            .Select(CreateResultBadge)
            .ToList();
    }

    private List<UpcomingFixtureRow> CreateUpcomingFixtureRows(League league, Team selectedTeam)
    {
        var fixtures = league.Fixtures
            .Where(fixture =>
                !fixture.IsPlayed &&
                IsTeamInFixture(fixture, selectedTeam) &&
                (_fixtureFilter is null || fixture.Competition == _fixtureFilter))
            .OrderBy(GetFixtureCalendarRound)
            .ThenBy(fixture => fixture.Competition)
            .Take(5)
            .Select(fixture =>
            {
                var isHome = fixture.HomeTeam == selectedTeam;
                var opponent = isHome ? fixture.AwayTeam : fixture.HomeTeam;
                var opponentLogoPath = GetClubLogoPath(opponent.Name);
                return new UpcomingFixtureRow
                {
                    RoundText = GetFixtureCardRoundText(fixture),
                    HomeAwayText = isHome ? "HOME" : "AWAY",
                    SummaryText = $"{CreateClubCode(opponent.Name)} {(isHome ? "H" : "A")} {CompetitionDisplayService.GetShortName(fixture.Competition)}",
                    OpponentShortName = CreateShortClubName(opponent.Name),
                    OpponentName = CreateTwoLineText(opponent.Name),
                    VenueText = CompetitionDisplayService.GetName(fixture.Competition),
                    OpponentLogoPath = opponentLogoPath,
                    OpponentLogoSource = CreateImageSource(opponentLogoPath),
                    HomeAwayBrush = CompetitionDisplayService.GetColor(fixture.Competition),
                    HomeAwayForeground = GetHomeAwayBadgeForeground(isHome)
                };
            })
            .ToList();

        return fixtures.Count == 0
            ? [new UpcomingFixtureRow
            {
                OpponentName = "No upcoming fixtures",
                OpponentShortName = "No fixtures",
                SummaryText = "No fixtures",
                VenueText = "Season schedule is complete.",
                RoundText = "-",
                HomeAwayText = "-",
                HomeAwayBrush = ThemeManager.GetBrushHex("AppSecondaryCardBackground", "#111827"),
                HomeAwayForeground = ThemeManager.GetBrushHex("AppMutedTextBrush", "#94A3B8")
            }]
            : fixtures;
    }

    private bool IsSeasonCompleted(League? league)
    {
        return league is not null &&
            (league.IsCompleted || _seasonCompletionService.IsSelectedTeamSeasonComplete(league, _state.SelectedTeam));
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

    private static string CreateShortClubName(string clubName)
    {
        var shortNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AFC Bournemouth"] = "Bournemouth",
            ["Brighton & Hove Albion"] = "Brighton",
            ["Manchester City"] = "Man City",
            ["Manchester United"] = "Man United",
            ["Newcastle United"] = "Newcastle",
            ["Nottingham Forest"] = "Nott'm Forest",
            ["Tottenham Hotspur"] = "Tottenham",
            ["West Ham United"] = "West Ham",
            ["Wolverhampton Wanderers"] = "Wolves"
        };

        if (shortNames.TryGetValue(clubName, out var shortName))
        {
            return shortName;
        }

        var cleanedName = clubName
            .Replace(" FC", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" CF", string.Empty, StringComparison.OrdinalIgnoreCase);

        if (cleanedName.Length <= 14)
        {
            return cleanedName;
        }

        var words = cleanedName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? cleanedName : words[0];
    }

    private static string FormatSeasonLabel(string season)
    {
        return string.IsNullOrWhiteSpace(season)
            ? "-"
            : season.Trim().Replace('-', '/');
    }

    private static bool IsTeamInFixture(Fixture fixture, Team team)
    {
        return fixture.HomeTeam.Name.Equals(team.Name, StringComparison.OrdinalIgnoreCase) ||
            fixture.AwayTeam.Name.Equals(team.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static Fixture? FindNextFixtureForTeamOrDefault(League league, Team selectedTeam)
    {
        return league.Fixtures
            .Where(fixture => !fixture.IsPlayed && IsTeamInFixture(fixture, selectedTeam))
            .OrderBy(GetFixtureCalendarRound)
            .ThenBy(fixture => fixture.Competition)
            .FirstOrDefault();
    }

    private static int GetFixtureCalendarRound(Fixture fixture)
    {
        return fixture.CalendarRound > 0 ? fixture.CalendarRound : fixture.RoundNumber;
    }

    private static string GetFixtureRoundText(Fixture fixture)
    {
        return string.IsNullOrWhiteSpace(fixture.RoundName)
            ? $"Round {fixture.RoundNumber}"
            : fixture.RoundName;
    }

    private static string GetFixtureCardRoundText(Fixture fixture)
    {
        if (fixture.Competition == CompetitionType.ChampionsLeague &&
            TryGetChampionsLeagueMatchday(fixture, out var matchday))
        {
            return $"UCL MD{matchday}";
        }

        return $"{CompetitionDisplayService.GetShortName(fixture.Competition)} {GetFixtureRoundText(fixture)}";
    }

    private static bool TryGetChampionsLeagueMatchday(Fixture fixture, out int matchday)
    {
        matchday = 0;
        var marker = "MD";
        var index = fixture.RoundName.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index >= 0 && int.TryParse(new string(fixture.RoundName[(index + marker.Length)..].TakeWhile(char.IsDigit).ToArray()), out matchday))
        {
            return true;
        }

        if (fixture.Competition == CompetitionType.ChampionsLeague && fixture.RoundNumber is >= 1 and <= 8)
        {
            matchday = fixture.RoundNumber;
            return true;
        }

        return false;
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        return (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
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

    private static string GetChampionsLeagueZoneBrush(int position)
    {
        if (position <= 8)
        {
            return "#3B82F6";
        }

        if (position <= 24)
        {
            return "#F97316";
        }

        return "#EF4444";
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

    private sealed class BracketRoundGroup
    {
        public string RoundName { get; init; } = string.Empty;
        public string SummaryText { get; init; } = string.Empty;
        public int SortOrder { get; init; }
        public IReadOnlyList<BracketMatchRow> Matches { get; init; } = [];
    }

    private sealed class BracketMatchRow
    {
        public string HomeTeamName { get; init; } = string.Empty;
        public string AwayTeamName { get; init; } = string.Empty;
        public string HomeLogoPath { get; init; } = string.Empty;
        public string AwayLogoPath { get; init; } = string.Empty;
        public string HomeSeedText { get; init; } = string.Empty;
        public string AwaySeedText { get; init; } = string.Empty;
        public string ScoreText { get; init; } = "vs";
        public string HomeFontWeight { get; init; } = "SemiBold";
        public string AwayFontWeight { get; init; } = "SemiBold";
        public string RowBackground { get; init; } = "#FFFFFF";
        public string BorderBrush { get; init; } = "#D8E0EA";
        public Thickness BorderThickness { get; init; } = new(1);
    }

    private sealed class UpcomingFixtureRow
    {
        public string RoundText { get; init; } = string.Empty;
        public string HomeAwayText { get; init; } = string.Empty;
        public string SummaryText { get; init; } = string.Empty;
        public string OpponentShortName { get; init; } = string.Empty;
        public string OpponentName { get; init; } = string.Empty;
        public string VenueText { get; init; } = string.Empty;
        public string OpponentLogoPath { get; init; } = string.Empty;
        public ImageSource? OpponentLogoSource { get; init; }
        public string HomeAwayBrush { get; init; } = "#E1E5EA";
        public string HomeAwayForeground { get; init; } = "#64748B";
    }

    private sealed record FixtureFilterOption(string Label, CompetitionType? Competition)
    {
        public override string ToString()
        {
            return Label;
        }
    }

    private enum DashboardTableView
    {
        League,
        ChampionsLeague
    }
}
