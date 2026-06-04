using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Views;

public partial class EndSeasonResultView : UserControl
{
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
        var selectedRow = _archive.FinalTable.FirstOrDefault(row =>
            row.TeamName.Equals(_state.SelectedTeam.Name, StringComparison.OrdinalIgnoreCase));
        ClubSummaryTextBlock.Text = selectedRow is null
            ? $"{_state.SelectedTeam.Name}: season finished"
            : $"{_state.SelectedTeam.Name}: {GetOrdinal(selectedRow.Position)} - {selectedRow.Points} pts, {selectedRow.Wins}W {selectedRow.Draws}D {selectedRow.Losses}L, GD {selectedRow.GoalDifference:+#;-#;0}";

        LoadOutcome(_archive);
        LoadLeagueOutcomes(_archive);
        LoadBudget(_archive.BudgetSummary);
        FinalTableDataGrid.ItemsSource = _archive.FinalTable.Select(row => new TableRow(row, _state.SelectedTeam.Name)).ToList();
        LoadStatTabs(_archive.PlayerStats);
        LoadAwards(_archive.Awards);
        HighlightsItemsControl.ItemsSource = _archive.Highlights;
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
        ChampionTextBlock.Text = champion is null
            ? "Champion: n/a"
            : $"{champion.TeamName} are {archive.LeagueName} Champions!";

        EuropeanTeamsItemsControl.ItemsSource = archive.FinalTable
            .Where(row => row.Position <= 6)
            .Select(row => row.Position switch
            {
                <= 4 => $"{row.Position}. {row.TeamName} - Champions League",
                5 => $"{row.Position}. {row.TeamName} - Europa League",
                6 => $"{row.Position}. {row.TeamName} - Conference League",
                _ => row.TeamName
            })
            .ToList();

        RelegatedTeamsItemsControl.ItemsSource = archive.FinalTable
            .OrderByDescending(row => row.Position)
            .Take(3)
            .OrderBy(row => row.Position)
            .Select(row => $"{row.Position}. {row.TeamName}")
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
        TopScorersDataGrid.ItemsSource = CreateStatRows(stats, stat => stat.Goals, "goals", includeZero: false);
        TopAssistsDataGrid.ItemsSource = CreateStatRows(stats, stat => stat.Assists, "assists", includeZero: false);
        MostSavesDataGrid.ItemsSource = CreateStatRows(
            stats.Where(stat => stat.Position == Position.Goalkeeper),
            stat => stat.Saves,
            "saves",
            includeZero: false);
        BestRatingDataGrid.ItemsSource = stats
            .Where(stat => stat.Appearances > 0)
            .OrderByDescending(stat => stat.AverageRating)
            .ThenByDescending(stat => stat.Appearances)
            .Take(15)
            .Select((stat, index) => StatRow.FromRating(index + 1, stat))
            .ToList();
        CleanSheetsDataGrid.ItemsSource = CreateStatRows(
            stats.Where(stat => stat.Position is Position.Goalkeeper or Position.Defender),
            stat => stat.CleanSheets,
            "clean sheets",
            includeZero: false);
        CardsDataGrid.ItemsSource = stats
            .Where(stat => stat.YellowCards > 0 || stat.RedCards > 0)
            .OrderByDescending(stat => stat.RedCards)
            .ThenByDescending(stat => stat.YellowCards)
            .Take(15)
            .Select((stat, index) => new StatRow(
                index + 1,
                stat.PlayerName,
                stat.TeamName,
                $"{stat.YellowCards}Y / {stat.RedCards}R",
                $"{stat.Appearances} apps"))
            .ToList();
    }

    private void LoadAwards(SeasonAwards awards)
    {
        PlayerOfSeasonTextBlock.Text = string.IsNullOrWhiteSpace(awards.PlayerOfTheSeason.PlayerName)
            ? "Player of the Season: n/a"
            : $"{awards.PlayerOfTheSeason.PlayerName} - {awards.PlayerOfTheSeason.TeamName}";
        PlayerOfSeasonSummaryTextBlock.Text = awards.PlayerOfTheSeason.Summary;
        YoungPlayerTextBlock.Text = string.IsNullOrWhiteSpace(awards.YoungPlayerOfTheSeason.PlayerName)
            ? "Young Player of the Season: n/a"
            : $"Young Player: {awards.YoungPlayerOfTheSeason.PlayerName} - {awards.YoungPlayerOfTheSeason.TeamName}";
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
        foreach (var grid in new[]
        {
            TopScorersDataGrid,
            TopAssistsDataGrid,
            MostSavesDataGrid,
            BestRatingDataGrid,
            CleanSheetsDataGrid,
            CardsDataGrid
        })
        {
            grid.Columns.Clear();
            grid.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new Binding(nameof(StatRow.Rank)), Width = 44 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Player", Binding = new Binding(nameof(StatRow.PlayerName)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Club", Binding = new Binding(nameof(StatRow.TeamName)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.Columns.Add(new DataGridTextColumn { Header = "Value", Binding = new Binding(nameof(StatRow.Value)), Width = 110 });
            grid.Columns.Add(new DataGridTextColumn { Header = "Detail", Binding = new Binding(nameof(StatRow.Detail)), Width = 120 });
        }
    }

    private static List<StatRow> CreateStatRows(
        IEnumerable<ArchivedPlayerStatRow> stats,
        Func<ArchivedPlayerStatRow, int> selector,
        string suffix,
        bool includeZero)
    {
        return stats
            .Where(stat => includeZero || selector(stat) > 0)
            .OrderByDescending(selector)
            .ThenByDescending(stat => stat.AverageRating)
            .Take(15)
            .Select((stat, index) => new StatRow(
                index + 1,
                stat.PlayerName,
                stat.TeamName,
                selector(stat).ToString(),
                $"{stat.Appearances} apps, {suffix}"))
            .ToList();
    }

    private static List<BestXiRow> CreateBestXiRows(IEnumerable<BestXiPlayer> bestXi)
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
                $"{player.AverageRating:0.00} avg",
                left,
                top);
        }).ToList();
    }

    private static (double Left, double Top) GetBestXiPosition(string slot, int occurrence)
    {
        return slot switch
        {
            "GK" => (302, 338),
            "RB" => (520, 260),
            "CB" => occurrence == 1 ? (232, 280) : (390, 280),
            "LB" => (84, 260),
            "CM" => occurrence == 1 ? (232, 174) : (390, 174),
            "CAM" => (376, 144),
            "RW" => (520, 54),
            "ST" => (302, 34),
            "LW" => (84, 54),
            _ => (302, 180)
        };
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

    private sealed record TableRow(ArchivedLeagueTableRow Source, string SelectedClubName)
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
    }

    private sealed record StatRow(int Rank, string PlayerName, string TeamName, string Value, string Detail)
    {
        public static StatRow FromRating(int rank, ArchivedPlayerStatRow stat)
        {
            return new StatRow(
                rank,
                stat.PlayerName,
                stat.TeamName,
                stat.AverageRating.ToString("0.00"),
                $"{stat.Appearances} apps");
        }
    }

    private sealed record BestXiRow(
        string Slot,
        string PlayerName,
        string TeamName,
        string Detail,
        double Left,
        double Top);
}
