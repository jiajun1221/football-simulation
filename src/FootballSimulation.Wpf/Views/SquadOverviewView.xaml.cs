using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FootballSimulation.Models;
using FootballSimulation.Services;
using FootballSimulation.Wpf.Helpers;
using FootballSimulation.Wpf.Services;
using FootballSimulation.Wpf.State;

namespace FootballSimulation.Wpf.Views;

public partial class SquadOverviewView : UserControl
{
    private readonly GameFlowState _state;
    private readonly Action<UserControl> _navigate;
    private readonly TransferMarketService _transferMarketService = new();
    private readonly SaveGameService _saveGameService = new();
    private readonly DispatcherTimer _toastTimer;
    private SquadPlayerRow? _selectedRow;

    public SquadOverviewView(GameFlowState state, Action<UserControl> navigate)
    {
        InitializeComponent();

        _state = state;
        _navigate = navigate;
        SquadDetailPanel.TransferListToggled += SquadDetailPanel_TransferListToggled;
        SquadDetailPanel.ContractRenewalRequested += SquadDetailPanel_ContractRenewalRequested;
        SquadDetailPanel.CaptainAssignmentRequested += SquadDetailPanel_CaptainAssignmentRequested;
        SquadDetailPanel.TransferLockToggled += SquadDetailPanel_TransferLockToggled;
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.4) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            CaptainToastBorder.Visibility = Visibility.Collapsed;
        };

        LoadSquad();
        LoadFormationEditor();
    }

    private void LoadSquad()
    {
        if (_state.SelectedTeam is null || _state.League is null)
        {
            return;
        }

        EnsureTransferState();
        var selectedPlayerId = _selectedRow?.Listing.Player.PlayerId;
        var rows = _transferMarketService
            .GetClubListings(_state.TransferMarket!, _state.League.LeagueId, _state.SelectedTeam, _state.League.PlayerStats)
            .Select(CreateSquadRow)
            .ToList();
        SquadDataGrid.ItemsSource = rows;

        _selectedRow = rows.FirstOrDefault(row => row.Listing.Player.PlayerId == selectedPlayerId) ?? rows.FirstOrDefault();
        SquadDataGrid.SelectedItem = _selectedRow;
        ShowSelectedPlayer();
    }

    private void BackToDashboardButton_Click(object sender, RoutedEventArgs e)
    {
        FormationSetupPanel.ApplyCurrentSetup();
        PersistCurrentSaveSlot();
        _navigate(new DashboardView(_state, _navigate));
    }

    private void SquadTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && int.TryParse(tag, out var tabIndex))
        {
            SquadTabControl.SelectedIndex = tabIndex;
        }
    }

    private void SquadTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SquadSegmentButton.IsChecked = SquadTabControl.SelectedIndex == 0;
        FormationSegmentButton.IsChecked = SquadTabControl.SelectedIndex == 1;
        if (SquadTabControl.SelectedIndex == 1)
        {
            LoadFormationEditor();
        }
    }

    private void LoadFormationEditor()
    {
        if (_state.SelectedTeam is null)
        {
            return;
        }

        FormationSetupPanel.LoadTeam(_state.SelectedTeam);
    }

    private void FormationSetupPanel_SetupChanged(object? sender, EventArgs e)
    {
        PersistCurrentSaveSlot();
    }

    private void SquadDetailPanel_CaptainAssignmentRequested(object? sender, EventArgs e)
    {
        if (_selectedRow is null || _state.SelectedTeam is null)
        {
            return;
        }

        var player = _selectedRow.Listing.Player;
        SetTeamCaptain(player);
        LoadSquad();
        ShowCaptainToast($"{player.Name} is now team captain.");
    }

    private void SquadDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedRow = SquadDataGrid.SelectedItem as SquadPlayerRow;
        ShowSelectedPlayer();
    }

    private void SquadDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit ||
            e.Row.Item is not SquadPlayerRow row ||
            e.EditingElement is not TextBox textBox)
        {
            return;
        }

        var value = textBox.Text.Trim().TrimStart('#');
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var shirtNumber) ||
            shirtNumber is < 1 or > 99)
        {
            RejectShirtNumberEdit("Please enter a shirt number between 1 and 99.");
            e.Cancel = true;
            return;
        }

        var squadPlayers = _state.SelectedTeam?.Players
            .Concat(_state.SelectedTeam.Substitutes)
            .Distinct()
            .ToList() ?? [];
        var editedPlayer = row.Listing.Player;
        var duplicate = squadPlayers.FirstOrDefault(player =>
            !ReferenceEquals(player, editedPlayer) &&
            player.SquadNumber == shirtNumber);
        if (duplicate is not null)
        {
            var replacementNumber = FindAvailableShirtNumber(squadPlayers, shirtNumber, editedPlayer, duplicate);
            if (replacementNumber is null)
            {
                RejectShirtNumberEdit($"#{shirtNumber} is already used by {duplicate.Name}, and no spare shirt number is available.");
                e.Cancel = true;
                return;
            }

            duplicate.SquadNumber = replacementNumber.Value;
        }

        editedPlayer.SquadNumber = shirtNumber;
        Dispatcher.BeginInvoke(() => LoadSquad());
    }

    private void SetTeamCaptain(Player captain)
    {
        if (_state.SelectedTeam is null)
        {
            return;
        }

        foreach (var player in _state.SelectedTeam.Players.Concat(_state.SelectedTeam.Substitutes).Distinct())
        {
            player.IsCaptain = ReferenceEquals(player, captain);
        }
    }

    private static int? FindAvailableShirtNumber(
        IReadOnlyCollection<Player> squadPlayers,
        int requestedNumber,
        Player editedPlayer,
        Player reassignedPlayer)
    {
        var unavailableNumbers = squadPlayers
            .Where(player => !ReferenceEquals(player, editedPlayer) && !ReferenceEquals(player, reassignedPlayer))
            .Select(player => player.SquadNumber)
            .Where(number => number is >= 1 and <= 99)
            .Append(requestedNumber)
            .ToHashSet();

        for (var number = 1; number <= 99; number++)
        {
            if (!unavailableNumbers.Contains(number))
            {
                return number;
            }
        }

        return null;
    }

    private void RejectShirtNumberEdit(string message)
    {
        MessageBox.Show(message, "Shirt Number", MessageBoxButton.OK, MessageBoxImage.Warning);
        Dispatcher.BeginInvoke(() => LoadSquad());
    }

    private void ShowCaptainToast(string message)
    {
        CaptainToastTextBlock.Text = message;
        CaptainToastBorder.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void ShowSelectedPlayer()
    {
        if (_selectedRow is null || _state.League is null || _state.SelectedTeam is null)
        {
            SquadDetailPanel.ShowEmpty();
            return;
        }

        var listing = _selectedRow.Listing;
        var player = listing.Player;
        var stat = _state.League.PlayerStats.FirstOrDefault(item => item.PlayerName == player.Name);
        var windowInfo = _transferMarketService.GetWindowInfo(_state.League, GetCurrentRound());
        var status = CreateStatusDisplay(player);

        SquadDetailPanel.ShowPlayer(new TransferPlayerDetailContext(
            listing,
            TransferDetailMode.Squad,
            stat,
            IsOwnPlayer: true,
            windowInfo.IsOpen,
            windowInfo.Tooltip,
            IsShortlisted: false,
            player.IsListedForSale,
            CanToggleShortlist: !HasAgreedTransfer(player),
            status.Text,
            status.Brush,
            status.Tooltip,
            CanAssignCaptain: true,
            IsCaptain: player.IsCaptain,
            CanToggleTransferLock: !HasAgreedTransfer(player),
            IsTransferLocked: player.RejectTransferOffers));
    }

    private void SquadDetailPanel_TransferLockToggled(object? sender, EventArgs e)
    {
        if (_selectedRow is null)
        {
            return;
        }

        var player = _selectedRow.Listing.Player;
        if (HasAgreedTransfer(player))
        {
            MessageBox.Show(
                "This player already has a transfer agreed for the next window.",
                "Transfer Agreed",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        player.RejectTransferOffers = !player.RejectTransferOffers;
        if (player.RejectTransferOffers && player.TransferStatus == PlayerTransferStatus.Listed)
        {
            player.TransferStatus = PlayerTransferStatus.None;
        }

        PersistCurrentSaveSlot();
        LoadSquad();
        ShowCaptainToast(player.RejectTransferOffers
            ? $"{player.Name} is now marked untouchable."
            : $"{player.Name} can now receive transfer offers.");
    }

    private void SquadDetailPanel_TransferListToggled(object? sender, EventArgs e)
    {
        if (_selectedRow is null)
        {
            return;
        }

        var player = _selectedRow.Listing.Player;
        if (HasAgreedTransfer(player))
        {
            MessageBox.Show(
                "This player already has a transfer agreed for the next window.",
                "Transfer Agreed",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!player.IsListedForSale && player.RejectTransferOffers)
        {
            player.RejectTransferOffers = false;
        }

        player.IsListedForSale = !player.IsListedForSale;
        PersistCurrentSaveSlot();
        LoadSquad();
    }

    private void SquadDetailPanel_ContractRenewalRequested(object? sender, EventArgs e)
    {
        if (_state.TransferMarket is null || _state.League is null || _state.SelectedTeam is null || _selectedRow is null)
        {
            return;
        }

        if (sender is not TransferPlayerDetailPanel panel || !TryParseThousandAmount(panel.RenewalWageText, out var weeklyWage))
        {
            MessageBox.Show(
                "Enter a valid weekly wage in thousands.",
                "Invalid Contract Offer",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var result = _transferMarketService.OfferContractExtension(
            _state.TransferMarket,
            _state.League.LeagueId,
            _state.SelectedTeam,
            _selectedRow.Listing.Player,
            weeklyWage,
            panel.RenewalYears,
            panel.RenewalRole,
            GetCurrentRound());

        LoadSquad();
        MessageBox.Show(
            result.Message,
            result.Accepted ? "Contract Extended" : "Contract Rejected",
            MessageBoxButton.OK,
            result.Accepted ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void PersistCurrentSaveSlot()
    {
        if (_state.CurrentSaveSlotNumber is not int slotNumber ||
            _state.League is null ||
            _state.SelectedTeam is null)
        {
            return;
        }

        try
        {
            var saveData = SaveGameService.CreateSaveData(_state.League, _state.SelectedTeam, _state.TransferMarket);
            _saveGameService.SaveGame(slotNumber, saveData);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            System.Diagnostics.Debug.WriteLine($"[SquadPresetSave] Could not persist formation preset: {ex.Message}");
        }
    }

    private void EnsureTransferState()
    {
        if (_state.League is null)
        {
            return;
        }

        _state.TransferMarket ??= _transferMarketService.CreateInitialState(_state.League);
        _transferMarketService.BindActiveLeague(_state.TransferMarket, _state.League);
    }

    private int GetCurrentRound()
    {
        if (_state.League is null || _state.SelectedTeam is null)
        {
            return 1;
        }

        return _state.League.Fixtures
            .Where(fixture => !fixture.IsPlayed && (fixture.HomeTeam == _state.SelectedTeam || fixture.AwayTeam == _state.SelectedTeam))
            .OrderBy(fixture => fixture.RoundNumber)
            .Select(fixture => fixture.RoundNumber)
            .FirstOrDefault(_state.League.Fixtures.Count == 0 ? 1 : _state.League.Fixtures.Max(fixture => fixture.RoundNumber));
    }

    private SquadPlayerRow CreateSquadRow(TransferPlayerListing listing)
    {
        var player = listing.Player;
        var formBadge = PlayerFormBadgeHelper.Create(player.FormStatus);
        var status = CreateStatusDisplay(player);
        var nationality = PlayerNationalityDisplayService.Resolve(player);

        return new SquadPlayerRow
        {
            Listing = listing,
            ShirtNumber = player.SquadNumber > 0 ? player.SquadNumber.ToString(CultureInfo.InvariantCulture) : string.Empty,
            CaptainBadgeVisibility = player.IsCaptain ? Visibility.Visible : Visibility.Collapsed,
            CaptainBadgeTooltip = $"{player.Name} is club captain",
            PlayerName = player.Name,
            NationalityFlagImagePath = nationality.FlagImagePath,
            NationalityName = nationality.Name,
            Position = player.PreferredPosition,
            Age = player.Age?.ToString(CultureInfo.InvariantCulture) ?? "-",
            OverallText = player.OverallRating.ToString(CultureInfo.InvariantCulture),
            Form = formBadge.Text,
            MarketValueText = TransferMarketService.FormatMoney(listing.MarketValue),
            ContractText = player.ContractEndYear?.ToString(CultureInfo.InvariantCulture) ?? "-",
            WageText = PlayerContractService.FormatWage(listing.WeeklyWage),
            Status = status.Text,
            StatusTooltip = status.Tooltip,
            StatusBrush = status.Brush,
            FormBrush = formBadge.Background,
            FormTextBrush = formBadge.Foreground
        };
    }

    private StatusDisplay CreateStatusDisplay(Player player)
    {
        var primaryStatus = GetPrimaryStatus(player);
        return new StatusDisplay(GetCompactStatusText(primaryStatus), GetStatusBrush(primaryStatus), primaryStatus);
    }

    private string GetPrimaryStatus(Player player)
    {
        if (player.IsSuspended)
        {
            return "Banned";
        }

        if (player.IsInjured || player.InjuryRecoveryMatches > 0)
        {
            return "Injured";
        }

        if (HasAgreedTransfer(player))
        {
            return "Transfer Agreed";
        }

        if (HasActiveAiOffer(player))
        {
            return "Offer Received";
        }

        if (HasActiveUserNegotiation(player) || player.TransferStatus == PlayerTransferStatus.Negotiating)
        {
            return "Negotiating";
        }

        if (player.TransferStatus == PlayerTransferStatus.Listed)
        {
            return "Listed";
        }

        if (player.RejectTransferOffers || player.TransferStatus == PlayerTransferStatus.Unavailable)
        {
            return "Untouchable";
        }

        return "Available";
    }

    private bool HasActiveAiOffer(Player player)
    {
        return _state.TransferMarket?.Offers.Any(offer =>
            !offer.IsUserOffer &&
            offer.PlayerId.Equals(player.PlayerId, StringComparison.OrdinalIgnoreCase) &&
            offer.Status is OfferStatus.Pending or OfferStatus.PendingUntilWindowOpens or OfferStatus.Countered) == true;
    }

    private bool HasActiveUserNegotiation(Player player)
    {
        return _state.TransferMarket?.Offers.Any(offer =>
            offer.IsUserOffer &&
            offer.PlayerId.Equals(player.PlayerId, StringComparison.OrdinalIgnoreCase) &&
            offer.Status is OfferStatus.Pending or OfferStatus.PendingUntilWindowOpens or OfferStatus.Countered) == true;
    }

    private bool HasAgreedTransfer(Player player)
    {
        return _state.TransferMarket?.Offers.Any(offer =>
            offer.PlayerId.Equals(player.PlayerId, StringComparison.OrdinalIgnoreCase) &&
            offer.Status == OfferStatus.AgreedForNextWindow) == true;
    }

    private static bool TryParseThousandAmount(string text, out decimal amount)
    {
        amount = 0;
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var thousands))
        {
            return false;
        }

        amount = Math.Max(0, thousands * 1_000m);
        return amount > 0;
    }

    private static string GetCompactStatusText(string status)
    {
        return status == "Offer Received" ? "Offer" : status;
    }

    private static string GetStatusBrush(string status)
    {
        return status switch
        {
            "Available" => "#10B981",
            "Untouchable" => "#061226",
            "Listed" => "#2563EB",
            "Offer Received" => "#8B5CF6",
            "Negotiating" => "#F59E0B",
            "Injured" => "#EF4444",
            "Banned" => "#991B1B",
            "Transfer Agreed" => "#0EA5E9",
            _ => "#10B981"
        };
    }

    private sealed class SquadPlayerRow
    {
        public required TransferPlayerListing Listing { get; init; }
        public string ShirtNumber { get; set; } = string.Empty;
        public Visibility CaptainBadgeVisibility { get; init; } = Visibility.Collapsed;
        public string CaptainBadgeTooltip { get; init; } = string.Empty;
        public string PlayerName { get; init; } = string.Empty;
        public string NationalityFlagImagePath { get; init; } = string.Empty;
        public string NationalityName { get; init; } = string.Empty;
        public string Position { get; init; } = string.Empty;
        public string Age { get; init; } = string.Empty;
        public string OverallText { get; init; } = string.Empty;
        public string Form { get; init; } = string.Empty;
        public string MarketValueText { get; init; } = string.Empty;
        public string ContractText { get; init; } = string.Empty;
        public string WageText { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string StatusTooltip { get; init; } = string.Empty;
        public string StatusBrush { get; init; } = "#10B981";
        public string FormBrush { get; init; } = "#FACC15";
        public string FormTextBrush { get; init; } = "#061226";
    }

    private sealed record StatusDisplay(string Text, string Brush, string Tooltip);
}
