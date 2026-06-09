using System.IO;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Services;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Views;

public partial class EndSeasonResultView : UserControl
{
    private const double DataGridHeaderHeight = 34;
    private const double DataGridRowHeight = 38;
    private const double DataGridChromePadding = 6;
    private const double FinalTableMinHeight = 250;
    private const double FinalTableMaxHeight = 700;
    private const int FinalTableScrollThreshold = 15;
    private const double StatTableMinHeight = 250;
    private const double StatTableMaxHeight = 600;
    private const int StatTableScrollThreshold = 10;
    private const int StandardStatMinimumAppearances = 5;
    private const int BestRatingMinimumAppearances = 10;
    private const double BestRatingMinimumLeagueMatchShare = 0.25;
    private const int GoldenGloveMinimumAppearances = 10;

    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;
    private readonly SeasonAwardsService _awardsService = new();
    private readonly SeasonRolloverService _rolloverService = new();
    private readonly SeasonCompletionService _completionService = new();
    private readonly ClubFinanceService _clubFinanceService = new();
    private readonly TransferMarketService _transferMarketService = new();
    private readonly SaveGameService _saveGameService = new();
    private SeasonArchive? _archive;

    public EndSeasonResultView(GameFlowState state, Action<UserControl> navigate)
    {
        InitializeComponent();
        _state = state;
        _navigate = navigate;

        ConfigureStatGrids();
        LoadSeasonResult();
    }

    private void LoadSeasonResult()
    {
        if (_state.League is null || _state.SelectedTeam is null)
        {
            return;
        }

        if (_state.League.PlayerStats.Count == 0)
        {
            new PlayerSeasonStatsService().RebuildLeagueSeasonStats(_state.League);
        }

        _archive = _awardsService.CreateArchive(_state.League, _state.SelectedTeam);
        _state.TransferMarket ??= _transferMarketService.CreateInitialState(_state.League);
        _transferMarketService.BindActiveLeague(_state.TransferMarket, _state.League);

        var selectedPosition = Math.Max(1, _archive.SelectedClubPosition);
        _archive.BudgetSummary = _clubFinanceService.PreviewSeasonRolloverBudget(
            _state.TransferMarket,
            _state.League.LeagueId,
            _state.SelectedTeam,
            selectedPosition,
            _archive.FinalTable.Count);

        SeasonTitleTextBlock.Text = $"Season {_archive.Season} Complete";
        LeagueSubtitleTextBlock.Text = $"{_archive.LeagueName} final results";
        HeroClubLogoImage.Source = CreateImageSource(GetClubLogoPath(_state.SelectedTeam.Name));
        var selectedRow = _archive.FinalTable.FirstOrDefault(row =>
            row.TeamName.Equals(_state.SelectedTeam.Name, StringComparison.OrdinalIgnoreCase));
        ClubSummaryTextBlock.Text = selectedRow is null
            ? $"{_state.SelectedTeam.Name}: season finished"
            : $"{_state.SelectedTeam.Name}: {GetOrdinal(selectedRow.Position)} - {selectedRow.Points} pts, {selectedRow.Wins}W {selectedRow.Draws}D {selectedRow.Losses}L, GD {selectedRow.GoalDifference:+#;-#;0}";

        LoadOutcome(_archive);
        LoadLeagueOutcomes(_archive);
        LoadBudget(_archive.BudgetSummary);
        var finalTableRows = _archive.FinalTable
            .Select(row => new TableRow(row, _state.SelectedTeam.Name, GetClubLogoPath(row.TeamName), _archive.FinalTable.Count))
            .ToList();
        FinalTableDataGrid.ItemsSource = finalTableRows;
        ApplyDataGridContentHeight(FinalTableDataGrid, finalTableRows.Count, FinalTableScrollThreshold, FinalTableMinHeight, FinalTableMaxHeight);
        LoadStatTabs(_archive.PlayerStats);
        LoadAwards(_archive.Awards, _archive.PlayerStats);
        HighlightsItemsControl.ItemsSource = CreateHighlightRows(_archive.Highlights, _archive.PlayerStats);
        BestXiItemsControl.ItemsSource = CreateBestXiRows(_archive.Awards.BestXi);
        FooterTextBlock.Text = "Press End Season to archive these results and create the next season.";
    }

    private void LoadOutcome(SeasonArchive archive)
    {
        OutcomeTitleTextBlock.Text = archive.SelectedClubOutcome;
        OutcomeSubtitleTextBlock.Text = CreateOutcomeSentence(archive);

        if (archive.SelectedClubPosition == 1)
        {
            HeroBorder.Background = ToBrush("#B7791F");
        }
        else if (archive.SelectedClubPosition > archive.FinalTable.Count - 3)
        {
            HeroBorder.Background = ToBrush("#991B1B");
        }
    }

    private void LoadLeagueOutcomes(SeasonArchive archive)
    {
        var champion = archive.FinalTable.FirstOrDefault();
        ChampionContentControl.Content = champion is null
            ? new OutcomeRow(0, "Champion: n/a", archive.LeagueName, GetClubLogoPath(string.Empty), "#FACC15")
            : new OutcomeRow(champion.Position, $"{champion.Position}. {champion.TeamName}", $"{archive.LeagueName} Champions", GetClubLogoPath(champion.TeamName), "#FACC15");

        EuropeanTeamsItemsControl.ItemsSource = archive.FinalTable
            .Where(row => row.Position <= 6)
            .Select(row => new OutcomeRow(
                row.Position,
                $"{row.Position}. {row.TeamName}",
                row.Position switch
                {
                    <= 4 => "Champions League",
                    5 => "Europa League",
                    6 => "Conference League",
                    _ => string.Empty
                },
                GetClubLogoPath(row.TeamName),
                row.Position switch
                {
                    <= 4 => "#2563EB",
                    5 => "#F97316",
                    6 => "#16A34A",
                    _ => "#64748B"
                }))
            .ToList();

        RelegatedTeamsItemsControl.ItemsSource = archive.FinalTable
            .OrderByDescending(row => row.Position)
            .Take(3)
            .OrderBy(row => row.Position)
            .Select(row => new OutcomeRow(
                row.Position,
                $"{row.Position}. {row.TeamName}",
                "Relegated",
                GetClubLogoPath(row.TeamName),
                "#DC2626"))
            .ToList();
    }

    private void LoadBudget(BudgetRolloverSummary summary)
    {
        BudgetSummaryTextBlock.Text = $"{FormatMoney(summary.NewBudget)} transfer budget";
        BudgetBreakdownTextBlock.Text =
            $"Carryover {FormatMoney(summary.RemainingCarryover)} + base {FormatMoney(summary.BaseBudget)} + performance {FormatMoney(summary.PerformanceBonus)} + qualification {FormatMoney(summary.QualificationBonus)}. {summary.Qualification}.";
    }

    private void LoadStatTabs(IReadOnlyList<ArchivedPlayerStatRow> stats)
    {
        var topScorers = CreateStatRows(stats, stat => stat.Goals, StandardStatMinimumAppearances, includeZero: false);
        TopScorersDataGrid.ItemsSource = topScorers;
        ApplyStatGridContentHeight(TopScorersDataGrid, topScorers.Count);

        var topAssists = CreateStatRows(stats, stat => stat.Assists, StandardStatMinimumAppearances, includeZero: false);
        TopAssistsDataGrid.ItemsSource = topAssists;
        ApplyStatGridContentHeight(TopAssistsDataGrid, topAssists.Count);

        var mostSaves = CreateStatRows(
            stats.Where(stat => stat.Position == Position.Goalkeeper),
            stat => stat.Saves,
            StandardStatMinimumAppearances,
            includeZero: false);
        MostSavesDataGrid.ItemsSource = mostSaves;
        ApplyStatGridContentHeight(MostSavesDataGrid, mostSaves.Count);

        var bestRatingMinimumAppearances = GetBestRatingMinimumAppearances();
        var bestRating = stats
            .Where(stat => stat.Appearances >= bestRatingMinimumAppearances)
            .OrderByDescending(stat => stat.AverageRating)
            .ThenByDescending(stat => stat.Appearances)
            .Take(15)
            .Select((stat, index) => CreateStatRow(index + 1, stat, stat.AverageRating.ToString("0.00")))
            .ToList();
        BestRatingDataGrid.ItemsSource = bestRating;
        ApplyStatGridContentHeight(BestRatingDataGrid, bestRating.Count);

        var cleanSheets = CreateStatRows(
            stats.Where(stat => stat.Position is Position.Goalkeeper or Position.Defender),
            stat => stat.CleanSheets,
            StandardStatMinimumAppearances,
            includeZero: false);
        CleanSheetsDataGrid.ItemsSource = cleanSheets;
        ApplyStatGridContentHeight(CleanSheetsDataGrid, cleanSheets.Count);

        var cards = stats
            .Where(stat => stat.YellowCards > 0 || stat.RedCards > 0)
            .OrderByDescending(stat => stat.RedCards)
            .ThenByDescending(stat => stat.YellowCards)
            .Take(15)
            .Select((stat, index) => CreateCardStatRow(index + 1, stat))
            .ToList();
        CardsDataGrid.ItemsSource = cards;
        ApplyStatGridContentHeight(CardsDataGrid, cards.Count);
    }

    private void LoadAwards(SeasonAwards awards, IReadOnlyList<ArchivedPlayerStatRow> stats)
    {
        var awardCards = new List<AwardCard>();
        AddAwardCard(awardCards, "Player of the Season", awards.PlayerOfTheSeason, stats);
        AddAwardCard(awardCards, "Young Player of the Season", awards.YoungPlayerOfTheSeason, stats);
        AddStatAwardCard(awardCards, "Top Scorer", stats
            .Where(stat => stat.Appearances >= StandardStatMinimumAppearances && stat.Goals > 0)
            .OrderByDescending(stat => stat.Goals)
            .ThenByDescending(stat => stat.AverageRating)
            .FirstOrDefault(), stat => $"{stat.Goals} goals");
        AddStatAwardCard(awardCards, "Assist King", stats
            .Where(stat => stat.Appearances >= StandardStatMinimumAppearances && stat.Assists > 0)
            .OrderByDescending(stat => stat.Assists)
            .ThenByDescending(stat => stat.AverageRating)
            .FirstOrDefault(), stat => $"{stat.Assists} assists");
        AddStatAwardCard(awardCards, "Golden Glove", stats
            .Where(stat => stat.Position == Position.Goalkeeper)
            .Where(stat => stat.Appearances >= GoldenGloveMinimumAppearances && stat.CleanSheets > 0)
            .OrderByDescending(stat => stat.CleanSheets)
            .ThenByDescending(stat => stat.Saves)
            .ThenByDescending(stat => stat.AverageRating)
            .FirstOrDefault(), stat => $"{stat.CleanSheets} clean sheets");
        AwardsItemsControl.ItemsSource = awardCards;
    }

    private void AddAwardCard(
        ICollection<AwardCard> awardCards,
        string title,
        SeasonAwardWinner winner,
        IReadOnlyList<ArchivedPlayerStatRow> stats)
    {
        if (string.IsNullOrWhiteSpace(winner.PlayerName))
        {
            return;
        }

        var stat = stats.FirstOrDefault(item => item.PlayerName.Equals(winner.PlayerName, StringComparison.OrdinalIgnoreCase));
        awardCards.Add(CreateAwardCard(
            title,
            winner.PlayerName,
            winner.TeamName,
            winner.Position,
            stat is null ? $"{winner.Score:0.0} score" : $"{stat.AverageRating:0.00} rating",
            stat is null ? winner.Summary : $"{stat.Goals} goals | {stat.Assists} assists",
            stat));
    }

    private void AddStatAwardCard(
        ICollection<AwardCard> awardCards,
        string title,
        ArchivedPlayerStatRow? stat,
        Func<ArchivedPlayerStatRow, string> keyStatFactory)
    {
        if (stat is null || string.IsNullOrWhiteSpace(stat.PlayerName))
        {
            return;
        }

        awardCards.Add(CreateAwardCard(
            title,
            stat.PlayerName,
            stat.TeamName,
            string.IsNullOrWhiteSpace(stat.ExactPosition) ? stat.Position.ToString() : stat.ExactPosition,
            keyStatFactory(stat),
            $"{stat.AverageRating:0.00} rating | {stat.Appearances} apps",
            stat));
    }

    private AwardCard CreateAwardCard(
        string title,
        string playerName,
        string teamName,
        string position,
        string keyStat,
        string detail,
        ArchivedPlayerStatRow? stat)
    {
        var clubLogo = GetClubLogoPath(teamName);
        return new AwardCard(
            title,
            playerName,
            clubLogo,
            GetPlayerIdentityImagePath(playerName, clubLogo),
            $"{teamName} | {position}",
            keyStat,
            detail);
    }

    private void EndSeasonButton_Click(object sender, RoutedEventArgs e)
    {
        if (_state.League is null || _state.SelectedTeam is null)
        {
            return;
        }

        try
        {
            _state.TransferMarket ??= _transferMarketService.CreateInitialState(_state.League);
            var result = _rolloverService.StartNextSeason(_state.League, _state.SelectedTeam, _state.TransferMarket);
            _state.League = result.League;
            _state.Teams = result.League.Teams;
            _state.SelectedTeam = result.SelectedTeam;
            _state.TransferMarket = result.TransferMarketState;
            _state.CurrentMatch = null;
            _state.CurrentFixture = _completionService.GetNextFixtureForTeamOrNull(result.League, result.SelectedTeam);

            SaveIfActiveSlot();
            _navigate(new DashboardView(_state, _navigate));
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            MessageBox.Show(
                $"The new season could not be started.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "End Season",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SaveIfActiveSlot()
    {
        if (_state.CurrentSaveSlotNumber is not int slotNumber ||
            _state.League is null ||
            _state.SelectedTeam is null)
        {
            return;
        }

        var saveData = SaveGameService.CreateSaveData(_state.League, _state.SelectedTeam, _state.TransferMarket);
        _saveGameService.SaveGame(slotNumber, saveData);
    }

    private void ConfigureStatGrids()
    {
        ConfigureStatGrid(TopScorersDataGrid, "Goals");
        ConfigureStatGrid(TopAssistsDataGrid, "Assists");
        ConfigureStatGrid(MostSavesDataGrid, "Saves");
        ConfigureRatingStatGrid(BestRatingDataGrid);
        ConfigureStatGrid(CleanSheetsDataGrid, "Clean Sheets");
        ConfigureCardsStatGrid(CardsDataGrid);
    }

    private void ConfigureStatGrid(DataGrid grid, string metricHeader)
    {
        ConfigureStatIdentityColumns(grid);
        grid.Columns.Add(new DataGridTextColumn { Header = "Apps", Binding = new Binding(nameof(StatRow.Apps)), Width = 58 });
        grid.Columns.Add(new DataGridTextColumn { Header = metricHeader, Binding = new Binding(nameof(StatRow.Value)), Width = 104 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Avg", Binding = new Binding(nameof(StatRow.AverageRatingText)), Width = 68 });
    }

    private void ConfigureRatingStatGrid(DataGrid grid)
    {
        ConfigureStatIdentityColumns(grid);
        grid.Columns.Add(new DataGridTextColumn { Header = "Apps", Binding = new Binding(nameof(StatRow.Apps)), Width = 58 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Rating", Binding = new Binding(nameof(StatRow.Value)), Width = 104 });
    }

    private void ConfigureCardsStatGrid(DataGrid grid)
    {
        ConfigureStatIdentityColumns(grid);
        grid.Columns.Add(new DataGridTextColumn { Header = "Apps", Binding = new Binding(nameof(StatRow.Apps)), Width = 58 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Yellow", Binding = new Binding(nameof(StatRow.YellowCards)), Width = 74 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Red", Binding = new Binding(nameof(StatRow.RedCards)), Width = 58 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Avg", Binding = new Binding(nameof(StatRow.AverageRatingText)), Width = 68 });
    }

    private void ConfigureStatIdentityColumns(DataGrid grid)
    {
        grid.Columns.Clear();
        grid.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new Binding(nameof(StatRow.Rank)), Width = 42 });
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Player",
            CellTemplate = (DataTemplate)FindResource("StatPlayerCellTemplate"),
            Width = new DataGridLength(1.65, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = "Club",
            CellTemplate = (DataTemplate)FindResource("StatClubCellTemplate"),
            Width = new DataGridLength(1.35, DataGridLengthUnitType.Star)
        });
    }

    private static void ApplyStatGridContentHeight(DataGrid grid, int rowCount)
    {
        ApplyDataGridContentHeight(grid, rowCount, StatTableScrollThreshold, StatTableMinHeight, StatTableMaxHeight);
    }

    private static void ApplyDataGridContentHeight(
        DataGrid grid,
        int rowCount,
        int scrollThreshold,
        double minHeight,
        double maxHeight)
    {
        var visibleRows = Math.Max(1, rowCount);
        var contentHeight = DataGridHeaderHeight + visibleRows * DataGridRowHeight + DataGridChromePadding;
        var desiredHeight = Math.Clamp(contentHeight, minHeight, maxHeight);
        grid.Height = desiredHeight;
        grid.MaxHeight = maxHeight;
        ScrollViewer.SetVerticalScrollBarVisibility(
            grid,
            rowCount > scrollThreshold ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled);
    }

    private List<StatRow> CreateStatRows(
        IEnumerable<ArchivedPlayerStatRow> stats,
        Func<ArchivedPlayerStatRow, int> selector,
        int minimumAppearances,
        bool includeZero)
    {
        return stats
            .Where(stat => stat.Appearances >= minimumAppearances)
            .Where(stat => includeZero || selector(stat) > 0)
            .OrderByDescending(selector)
            .ThenByDescending(stat => stat.AverageRating)
            .Take(15)
            .Select((stat, index) => CreateStatRow(index + 1, stat, selector(stat).ToString()))
            .ToList();
    }

    private StatRow CreateStatRow(int rank, ArchivedPlayerStatRow stat, string value)
    {
        return new StatRow(
            rank,
            stat.PlayerName,
            string.IsNullOrWhiteSpace(stat.ExactPosition) ? stat.Position.ToString() : stat.ExactPosition,
            stat.TeamName,
            GetClubLogoPath(stat.TeamName),
            stat.Appearances,
            value,
            stat.AverageRating.ToString("0.00"),
            stat.YellowCards,
            stat.RedCards);
    }

    private StatRow CreateCardStatRow(int rank, ArchivedPlayerStatRow stat)
    {
        return CreateStatRow(rank, stat, string.Empty);
    }

    private int GetBestRatingMinimumAppearances()
    {
        var leagueMatches = _archive is not null && _archive.FinalTable.Count > 0
            ? _archive.FinalTable.Max(row => row.Played)
            : 0;
        if (leagueMatches <= 0 && _state.League is not null)
        {
            leagueMatches = Math.Max(1, _state.League.Teams.Count - 1) * 2;
        }

        var shareThreshold = Math.Max(1, (int)Math.Ceiling(leagueMatches * BestRatingMinimumLeagueMatchShare));
        return Math.Min(BestRatingMinimumAppearances, shareThreshold);
    }

    private List<BestXiRow> CreateBestXiRows(IEnumerable<BestXiPlayer> bestXi)
    {
        var slotCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return bestXi.Select(player =>
        {
            slotCounts[player.Slot] = slotCounts.GetValueOrDefault(player.Slot) + 1;
            var (left, top) = GetBestXiPosition(player.Slot, slotCounts[player.Slot]);
            return new BestXiRow(
                player.Slot,
                player.PlayerName,
                player.TeamName,
                GetClubLogoPath(player.TeamName),
                CreateBestXiDetail(player),
                left,
                top);
        }).ToList();
    }

    private static (double Left, double Top) GetBestXiPosition(string slot, int occurrence)
    {
        return slot switch
        {
            "GK" => (310, 460),
            "RB" => (585, 350),
            "CB" => occurrence == 1 ? (225, 372) : (395, 372),
            "LB" => (55, 350),
            "CM" => occurrence == 1 ? (185, 238) : (435, 238),
            "CAM" => (310, 156),
            "RW" => (555, 58),
            "ST" => (310, 28),
            "LW" => (65, 58),
            _ => (310, 260)
        };
    }

    private static string CreateBestXiDetail(BestXiPlayer player)
    {
        var keyStat = player.Slot switch
        {
            "GK" => $"{player.Saves} saves",
            "CB" or "LB" or "RB" => player.Goals > 0 ? $"{player.Goals} goals" : $"{player.AverageRating:0.00} avg",
            "CM" or "CAM" => player.Assists >= player.Goals ? $"{player.Assists} assists" : $"{player.Goals} goals",
            _ => player.Goals > 0 ? $"{player.Goals} goals" : $"{player.AverageRating:0.00} avg"
        };

        return $"{player.AverageRating:0.00} avg | {keyStat}";
    }

    private static string CreateOutcomeSentence(SeasonArchive archive)
    {
        return archive.SelectedClubPosition switch
        {
            1 => $"{archive.SelectedClubName} lifted the title.",
            <= 4 and > 1 => $"{archive.SelectedClubName} qualified for the Champions League.",
            5 => $"{archive.SelectedClubName} qualified for the Europa League.",
            6 => $"{archive.SelectedClubName} qualified for the Conference League.",
            _ when archive.SelectedClubPosition > archive.FinalTable.Count - 3 => $"{archive.SelectedClubName} finished in the relegation places.",
            _ => $"{archive.SelectedClubName} finished {GetOrdinal(archive.SelectedClubPosition)}."
        };
    }

    private static string FormatMoney(decimal value)
    {
        var sign = value < 0 ? "-" : string.Empty;
        value = Math.Abs(value);
        return value >= 1_000_000m
            ? $"{sign}${value / 1_000_000m:0.#}M"
            : $"{sign}${value / 1_000m:0.#}K";
    }

    private static string GetOrdinal(int value)
    {
        if (value <= 0)
        {
            return "n/a";
        }

        var suffix = value % 100 is 11 or 12 or 13
            ? "th"
            : (value % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            };
        return $"{value}{suffix}";
    }

    private static SolidColorBrush ToBrush(string hex)
    {
        return (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;
    }

    private List<HighlightRow> CreateHighlightRows(IEnumerable<SeasonHighlight> highlights, IReadOnlyList<ArchivedPlayerStatRow> stats)
    {
        return highlights
            .Select(highlight =>
            {
                var playerStat = stats.FirstOrDefault(stat =>
                    highlight.PrimaryText.Contains(stat.PlayerName, StringComparison.OrdinalIgnoreCase) ||
                    highlight.SecondaryText.Contains(stat.PlayerName, StringComparison.OrdinalIgnoreCase));
                var imagePath = playerStat is not null
                    ? GetPlayerIdentityImagePath(playerStat.PlayerName, GetClubLogoPath(playerStat.TeamName))
                    : GetClubLogoPath(FindMentionedTeamName($"{highlight.PrimaryText} {highlight.SecondaryText}") ?? string.Empty);

                return new HighlightRow(
                    highlight.Title,
                    highlight.PrimaryText,
                    highlight.SecondaryText,
                    imagePath);
            })
            .ToList();
    }

    private string? FindMentionedTeamName(string text)
    {
        return _state.League?.Teams
            .Select(team => team.Name)
            .OrderByDescending(name => name.Length)
            .FirstOrDefault(teamName => text.Contains(teamName, StringComparison.OrdinalIgnoreCase));
    }

    private string GetClubLogoPath(string teamName)
    {
        return ClubLogoService.GetClubLogoPath(teamName, _state.League?.LeagueId ?? _state.SelectedLeagueId);
    }

    private string GetPlayerIdentityImagePath(string playerName, string clubLogoPath)
    {
        var playerImage = $"pack://application:,,,/Assets/Players/{CreatePlayerImageSlug(playerName)}.png";
        if (ResourceExists(playerImage))
        {
            return playerImage;
        }

        if (!string.IsNullOrWhiteSpace(clubLogoPath))
        {
            return clubLogoPath;
        }

        const string defaultImage = "pack://application:,,,/Assets/Players/default.png";
        return ResourceExists(defaultImage) ? defaultImage : string.Empty;
    }

    private static string CreatePlayerImageSlug(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
        {
            return "default";
        }

        var normalized = playerName.Normalize(NormalizationForm.FormD);
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

    private static ImageSource? CreateImageSource(string imagePath)
    {
        return string.IsNullOrWhiteSpace(imagePath)
            ? null
            : new ImageSourceConverter().ConvertFromString(imagePath) as ImageSource;
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

    private sealed record TableRow(ArchivedLeagueTableRow Source, string SelectedClubName, string LogoPath, int TeamCount)
    {
        public int Position => Source.Position;
        public string TeamName => Source.TeamName;
        public int Played => Source.Played;
        public int Wins => Source.Wins;
        public int Draws => Source.Draws;
        public int Losses => Source.Losses;
        public int GoalDifference => Source.GoalDifference;
        public int Points => Source.Points;
        public bool IsSelectedClub => Source.TeamName.Equals(SelectedClubName, StringComparison.OrdinalIgnoreCase);
        public string ZoneBrush => Position switch
        {
            <= 4 => "#2563EB",
            5 => "#F97316",
            6 => "#16A34A",
            _ when Position > TeamCount - 3 => "#DC2626",
            _ => "Transparent"
        };
        public string RowBackground => IsSelectedClub ? "#FFF7CC" : "Transparent";
        public FontWeight RowFontWeight => IsSelectedClub ? FontWeights.Black : FontWeights.Normal;
    }

    private sealed record OutcomeRow(int Position, string PrimaryText, string SecondaryText, string LogoPath, string ZoneBrush);

    private sealed record AwardCard(
        string Title,
        string PlayerName,
        string ClubLogoPath,
        string IdentityImagePath,
        string MetaText,
        string KeyStat,
        string Detail);

    private sealed record HighlightRow(string Title, string PrimaryText, string SecondaryText, string ImagePath);

    private sealed record StatRow(
        int Rank,
        string PlayerName,
        string PositionText,
        string TeamName,
        string ClubLogoPath,
        int Apps,
        string Value,
        string AverageRatingText,
        int YellowCards,
        int RedCards);

    private sealed record BestXiRow(
        string Slot,
        string PlayerName,
        string TeamName,
        string ClubLogoPath,
        string Detail,
        double Left,
        double Top);
}
