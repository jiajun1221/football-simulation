using System.Globalization;
using System.Text;
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
    private readonly LeagueDataService _leagueDataService = new();
    private readonly PlayerMarketValueCalculator _marketValueCalculator = new();
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
        var restoredPlayers = RestoreMissingSelectedTeamSourcePlayers() |
            RestoreMissingCompletedIncomingTransfers() |
            RestoreMissingSelectedTeamStatPlayers();
        if (restoredPlayers)
        {
            EnsureTransferState();
            PersistCurrentSaveSlot();
        }

        var selectedPlayerId = _selectedRow?.Listing.Player.PlayerId;
        var listings = _transferMarketService
            .GetClubListings(_state.TransferMarket!, _state.League.LeagueId, _state.SelectedTeam, _state.League.PlayerStats)
            .ToList();
        AddMissingRosterListings(listings);

        var rows = listings
            .OrderByDescending(listing => listing.Player.OverallRating)
            .ThenBy(listing => listing.Player.Name)
            .Select(CreateSquadRow)
            .ToList();
        SquadDataGrid.ItemsSource = rows;

        _selectedRow = rows.FirstOrDefault(row => row.Listing.Player.PlayerId == selectedPlayerId) ?? rows.FirstOrDefault();
        SquadDataGrid.SelectedItem = _selectedRow;
        ShowSelectedPlayer();
    }

    private bool RestoreMissingSelectedTeamSourcePlayers()
    {
        if (_state.SelectedTeam is null || _state.League is null)
        {
            return false;
        }

        var currentPlayers = _state.SelectedTeam.Players.Concat(_state.SelectedTeam.Substitutes).Distinct().ToList();
        var restoredAny = false;
        var sourceTeam = LoadSourceTeam(_state.League.LeagueId, _state.SelectedTeam.Name);
        if (sourceTeam is null)
        {
            return false;
        }

        foreach (var sourcePlayer in sourceTeam.Players.Concat(sourceTeam.Substitutes))
        {
            if (RosterContainsPlayer(currentPlayers, sourcePlayer.PlayerId, sourcePlayer.Name) ||
                HasCompletedOutgoingTransfer(sourcePlayer.Name))
            {
                continue;
            }

            PrepareRestoredPlayer(sourcePlayer);
            _state.SelectedTeam.Substitutes.Add(sourcePlayer);
            currentPlayers.Add(sourcePlayer);
            restoredAny = true;
        }

        return restoredAny;
    }

    private Team? LoadSourceTeam(string leagueId, string clubName)
    {
        try
        {
            return _leagueDataService.LoadTeams(leagueId)
                .FirstOrDefault(team => team.Name.Equals(clubName, StringComparison.OrdinalIgnoreCase));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private bool RestoreMissingCompletedIncomingTransfers()
    {
        if (_state.SelectedTeam is null || _state.League is null || _state.TransferMarket is null)
        {
            return false;
        }

        var currentPlayers = _state.SelectedTeam.Players.Concat(_state.SelectedTeam.Substitutes).Distinct().ToList();
        var incomingTransfers = _state.TransferMarket.TransferHistory
            .Where(transfer => transfer.ToClubName.Equals(_state.SelectedTeam.Name, StringComparison.OrdinalIgnoreCase) &&
                transfer.ToLeagueId.Equals(_state.League.LeagueId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(transfer => transfer.RoundNumber)
            .GroupBy(CreateTransferPlayerKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        var restoredAny = false;

        foreach (var transfer in incomingTransfers)
        {
            if (RosterContainsPlayer(currentPlayers, transfer.PlayerId, transfer.PlayerName) ||
                HasLaterOutgoingTransfer(transfer))
            {
                continue;
            }

            var sourcePlayer = LoadSourceTransferPlayer(transfer);
            if (sourcePlayer is null)
            {
                continue;
            }

            PrepareRestoredIncomingTransferPlayer(sourcePlayer, transfer);
            _state.SelectedTeam.Substitutes.Add(sourcePlayer);
            currentPlayers.Add(sourcePlayer);
            restoredAny = true;
        }

        return restoredAny;
    }

    private bool RestoreMissingSelectedTeamStatPlayers()
    {
        if (_state.SelectedTeam is null || _state.League is null)
        {
            return false;
        }

        var currentPlayers = _state.SelectedTeam.Players.Concat(_state.SelectedTeam.Substitutes).Distinct().ToList();
        var sourceTeam = LoadSourceTeam(_state.League.LeagueId, _state.SelectedTeam.Name);
        var sourcePlayers = sourceTeam?.Players.Concat(sourceTeam.Substitutes).ToList() ?? [];
        var restoredAny = false;

        foreach (var stat in _state.League.PlayerStats
            .Where(stat => stat.TeamName.Equals(_state.SelectedTeam.Name, StringComparison.OrdinalIgnoreCase))
            .Where(stat => stat.Appearances > 0 || stat.MinutesPlayed > 0)
            .OrderByDescending(stat => stat.MinutesPlayed))
        {
            if (RosterContainsPlayer(currentPlayers, stat.PlayerId, stat.PlayerName) ||
                RosterContainsPlayer(sourcePlayers, stat.PlayerId, stat.PlayerName) ||
                WasCompletedIncomingTransfer(stat.PlayerName) ||
                HasCompletedOutgoingTransfer(stat.PlayerName))
            {
                continue;
            }

            var player = CreateRecoveredPlayerFromStats(stat);
            _state.SelectedTeam.Substitutes.Add(player);
            currentPlayers.Add(player);
            RecordRecoveredAcademyHistory(player, stat);
            restoredAny = true;
        }

        return restoredAny;
    }

    private Player CreateRecoveredPlayerFromStats(PlayerSeasonStats stat)
    {
        if (_state.SelectedTeam is null || _state.League is null)
        {
            throw new InvalidOperationException("No selected team is available for player recovery.");
        }

        var exactPosition = string.IsNullOrWhiteSpace(stat.ExactPosition)
            ? GetDefaultExactPosition(stat.Position)
            : stat.ExactPosition;
        var overall = EstimateRecoveredOverall(stat);
        var player = new Player
        {
            PlayerId = ResolveRecoveredPlayerId(stat),
            Name = stat.PlayerName,
            SquadNumber = ResolveRecoveredSquadNumber(stat),
            Position = stat.Position,
            PreferredPosition = exactPosition,
            AssignedPosition = exactPosition,
            SecondaryPositions = GetRecoveredSecondaryPositions(exactPosition),
            NationalityCode = GuessNationalityCode(stat.PlayerName),
            NationalityName = GuessNationalityName(stat.PlayerName),
            FlagImagePath = GuessFlagPath(stat.PlayerName),
            OverallRating = overall,
            BaseOverallRating = overall,
            Age = 18,
            PotentialOverall = Math.Clamp(overall + 10, overall, 94),
            Role = PlayerRole.Prospect,
            Form = "Average",
            CurrentForm = 50,
            FormStatus = PlayerFormStatus.Average,
            Morale = 55,
            Stamina = Math.Clamp(100 - GetRecentFatigueForRecoveredPlayer(stat.PlayerName), 62, 100),
            TransferStatus = PlayerTransferStatus.None,
            ClubId = CreateClubId(_state.League.LeagueId, _state.SelectedTeam.Name),
            IsStarter = false,
            IsOnPitch = false
        };

        var attributes = PlayerAttributeService.DeriveAttributes(
            player.Position,
            player.PreferredPosition,
            player.OverallRating,
            player.Traits,
            (int)Math.Round(player.Stamina));
        player.Pace = attributes.Pace;
        player.Shooting = attributes.Shooting;
        player.Passing = attributes.Passing;
        player.Dribbling = attributes.Dribbling;
        player.Defending = attributes.Defending;
        player.Physical = attributes.Physical;
        YouthAcademyService.RepairSeniorOverallAttributes(player, overall);
        PlayerContractService.EnsureContract(player, _state.League.LeagueId, GetSeasonEndYear(_state.League.Season));
        player.WeeklyWage ??= PlayerContractService.EstimateWeeklyWage(player, _state.League.LeagueId);
        player.ReleaseClause = PlayerContractService.EstimateReleaseClause(player, 0, _state.League.LeagueId);
        return player;
    }

    private void RecordRecoveredAcademyHistory(Player player, PlayerSeasonStats stat)
    {
        if (_state.SelectedTeam is null || _state.League is null)
        {
            return;
        }

        var academy = _state.League.YouthAcademies.FirstOrDefault(item =>
            item.ClubName.Equals(_state.SelectedTeam.Name, StringComparison.OrdinalIgnoreCase));
        if (academy is null)
        {
            return;
        }

        academy.AcademyHistory ??= [];
        if (academy.AcademyHistory.Any(record =>
            record.EventType == AcademyHistoryEventType.Promoted &&
            (record.PlayerId.Equals(player.PlayerId, StringComparison.OrdinalIgnoreCase) ||
                record.PlayerName.Equals(player.Name, StringComparison.OrdinalIgnoreCase))))
        {
            return;
        }

        academy.AcademyHistory.Add(new AcademyHistoryRecord
        {
            EventType = AcademyHistoryEventType.Promoted,
            Season = _state.League.Season,
            CalendarRound = GetCurrentRound(),
            PlayerId = player.PlayerId,
            PlayerName = player.Name,
            NationalityCode = player.NationalityCode,
            NationalityName = player.NationalityName,
            FlagImagePath = player.FlagImagePath,
            Age = player.Age ?? 18,
            Position = player.Position,
            PreferredPosition = player.PreferredPosition,
            Overall = player.OverallRating,
            PotentialMin = player.OverallRating,
            PotentialMax = player.PotentialOverall ?? player.OverallRating,
            DevelopmentRate = YouthDevelopmentRate.Normal,
            Source = "Recovered Senior Squad",
            Notes = $"Restored from saved Chelsea stats after missing from senior squad. Apps: {stat.Appearances}, minutes: {stat.MinutesPlayed}."
        });
    }

    private Player? LoadSourceTransferPlayer(TransferHistoryItem transfer)
    {
        if (string.IsNullOrWhiteSpace(transfer.FromLeagueId) ||
            string.IsNullOrWhiteSpace(transfer.FromClubName))
        {
            return null;
        }

        try
        {
            var sourceTeam = LoadSourceTeam(transfer.FromLeagueId, transfer.FromClubName);
            return sourceTeam?.Players
                .Concat(sourceTeam.Substitutes)
                .FirstOrDefault(player => IsTransferPlayerMatch(player, transfer));
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private void PrepareRestoredIncomingTransferPlayer(Player player, TransferHistoryItem transfer)
    {
        PrepareRestoredPlayer(player);
        player.PlayerId = string.IsNullOrWhiteSpace(transfer.PlayerId) ? player.PlayerId : transfer.PlayerId;
        player.TransferStatus = PlayerTransferStatus.RecentlyTransferred;
        player.PreviousClubId = string.IsNullOrWhiteSpace(transfer.FromClubId)
            ? CreateClubId(transfer.FromLeagueId, transfer.FromClubName)
            : transfer.FromClubId;
        player.LastTransferRound = transfer.RoundNumber;
        player.WeeklyWage = transfer.WeeklyWage ?? player.WeeklyWage;
        player.ContractEndYear = transfer.ContractEndYear ?? player.ContractEndYear;
        player.Role = transfer.SquadRole ?? player.Role;
    }

    private bool HasLaterOutgoingTransfer(TransferHistoryItem incomingTransfer)
    {
        if (_state.TransferMarket is null || _state.SelectedTeam is null)
        {
            return false;
        }

        var transferKey = CreateTransferPlayerKey(incomingTransfer);
        return _state.TransferMarket.TransferHistory.Any(transfer =>
                CreateTransferPlayerKey(transfer).Equals(transferKey, StringComparison.OrdinalIgnoreCase) &&
                transfer.FromClubName.Equals(_state.SelectedTeam.Name, StringComparison.OrdinalIgnoreCase) &&
                !transfer.ToClubName.Equals(_state.SelectedTeam.Name, StringComparison.OrdinalIgnoreCase) &&
                transfer.RoundNumber >= incomingTransfer.RoundNumber) ||
            HasCompletedOutgoingTransfer(incomingTransfer.PlayerName);
    }

    private bool WasCompletedIncomingTransfer(string playerName)
    {
        if (_state.TransferMarket is null || _state.SelectedTeam is null)
        {
            return false;
        }

        return _state.TransferMarket.TransferHistory.Any(transfer =>
            IsSamePlayerName(transfer.PlayerName, playerName) &&
            transfer.ToClubName.Equals(_state.SelectedTeam.Name, StringComparison.OrdinalIgnoreCase));
    }

    private string ResolveRecoveredPlayerId(PlayerSeasonStats stat)
    {
        if (!string.IsNullOrWhiteSpace(stat.PlayerId))
        {
            return stat.PlayerId;
        }

        var offerPlayerId = _state.TransferMarket?.Offers
            .Where(offer => IsSamePlayerName(offer.PlayerName, stat.PlayerName))
            .Where(offer => offer.FromClubName.Equals(_state.SelectedTeam?.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                offer.ToClubName.Equals(_state.SelectedTeam?.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(offer => offer.CompletedRound ?? offer.AgreementRound ?? offer.CreatedRound)
            .Select(offer => offer.PlayerId)
            .FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
        if (!string.IsNullOrWhiteSpace(offerPlayerId))
        {
            return offerPlayerId;
        }

        return $"{_state.League?.LeagueId ?? LeagueDataService.DefaultLeagueId}:{NormalizePlayerName(_state.SelectedTeam?.Name ?? "club")}:{NormalizePlayerName(stat.PlayerName)}";
    }

    private int ResolveRecoveredSquadNumber(PlayerSeasonStats stat)
    {
        var playerId = ResolveRecoveredPlayerId(stat);
        var idNumber = playerId.Split(':', '-', StringSplitOptions.RemoveEmptyEntries)
            .Reverse()
            .Select(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0)
            .FirstOrDefault(number => number is >= 1 and <= 99);
        if (idNumber > 0 && !IsSquadNumberUsed(idNumber))
        {
            return idNumber;
        }

        for (var number = 1; number <= 99; number++)
        {
            if (!IsSquadNumberUsed(number))
            {
                return number;
            }
        }

        return 0;
    }

    private bool IsSquadNumberUsed(int squadNumber)
    {
        return _state.SelectedTeam?.Players
            .Concat(_state.SelectedTeam.Substitutes)
            .Any(player => player.SquadNumber == squadNumber) == true;
    }

    private int GetRecentFatigueForRecoveredPlayer(string playerName)
    {
        return _state.CurrentMatch?.PlayerPerformances
            .Where(performance => IsSamePlayerName(performance.PlayerName, playerName))
            .OrderByDescending(performance => performance.FatigueAtEnd)
            .Select(performance => performance.FatigueAtEnd)
            .FirstOrDefault() ?? 0;
    }

    private static int EstimateRecoveredOverall(PlayerSeasonStats stat)
    {
        if (stat.AverageRating <= 0)
        {
            return stat.Position == Position.Forward ? 68 : 65;
        }

        var performanceScore = 63 + (stat.AverageRating - 6.0) * 8;
        var usageBonus = Math.Min(6, stat.Starts / 4);
        var outputBonus = Math.Min(5, stat.Goals + stat.Assists);
        return Math.Clamp((int)Math.Round(performanceScore + usageBonus + outputBonus), 58, 82);
    }

    private static string GetDefaultExactPosition(Position position)
    {
        return position switch
        {
            Position.Goalkeeper => "GK",
            Position.Defender => "CB",
            Position.Midfielder => "CM",
            Position.Forward => "CF",
            _ => "CM"
        };
    }

    private static List<string> GetRecoveredSecondaryPositions(string exactPosition)
    {
        return exactPosition switch
        {
            "CF" => ["ST", "CAM"],
            "ST" => ["CF", "LW", "RW"],
            "LW" => ["RW", "ST"],
            "RW" => ["LW", "ST"],
            "CM" => ["CAM", "CDM"],
            "CAM" => ["CM", "CF"],
            "CDM" => ["CM", "CB"],
            "LB" => ["LWB", "CB"],
            "RB" => ["RWB", "CB"],
            "CB" => ["CDM"],
            _ => []
        };
    }

    private static string GuessNationalityCode(string playerName)
    {
        return playerName.Contains("Alves", StringComparison.OrdinalIgnoreCase) ||
            playerName.Contains("Silva", StringComparison.OrdinalIgnoreCase) ||
            playerName.Contains("Costa", StringComparison.OrdinalIgnoreCase)
            ? "BR"
            : string.Empty;
    }

    private static string GuessNationalityName(string playerName)
    {
        return GuessNationalityCode(playerName) == "BR" ? "Brazil" : string.Empty;
    }

    private static string GuessFlagPath(string playerName)
    {
        return GuessNationalityCode(playerName) == "BR" ? "/Assets/Flags/brazil.png" : string.Empty;
    }

    private void PrepareRestoredPlayer(Player player)
    {
        if (_state.SelectedTeam is null || _state.League is null)
        {
            return;
        }

        player.IsStarter = false;
        player.IsOnPitch = false;
        player.TransferStatus = PlayerTransferStatus.None;
        player.ClubId = CreateClubId(_state.League.LeagueId, _state.SelectedTeam.Name);
        player.PreviousClubId = string.Empty;
        player.LastTransferRound = null;
        player.LastTransferWindowId = string.Empty;
        player.RejectTransferOffers = false;
        PlayerContractService.EnsureContract(player, _state.League.LeagueId, GetSeasonEndYear(_state.League.Season));
    }

    private static bool RosterContainsPlayer(IEnumerable<Player> currentPlayers, string playerId, string playerName)
    {
        return currentPlayers.Any(player =>
            !string.IsNullOrWhiteSpace(playerId) &&
            player.PlayerId.Equals(playerId, StringComparison.OrdinalIgnoreCase) ||
            IsSamePlayerName(player.Name, playerName));
    }

    private static bool IsTransferPlayerMatch(Player player, TransferHistoryItem transfer)
    {
        return !string.IsNullOrWhiteSpace(transfer.PlayerId) &&
            player.PlayerId.Equals(transfer.PlayerId, StringComparison.OrdinalIgnoreCase) ||
            IsSamePlayerName(player.Name, transfer.PlayerName);
    }

    private static string CreateTransferPlayerKey(TransferHistoryItem transfer)
    {
        return string.IsNullOrWhiteSpace(transfer.PlayerId)
            ? NormalizePlayerName(transfer.PlayerName)
            : transfer.PlayerId;
    }

    private bool HasCompletedOutgoingTransfer(string playerName)
    {
        if (_state.TransferMarket is null || _state.SelectedTeam is null)
        {
            return false;
        }

        return _state.TransferMarket.Offers.Any(offer =>
            IsSamePlayerName(offer.PlayerName, playerName) &&
            offer.FromClubName.Equals(_state.SelectedTeam.Name, StringComparison.OrdinalIgnoreCase) &&
            offer.Status is OfferStatus.Completed or OfferStatus.CompletedWhenWindowOpens);
    }

    private static bool IsSamePlayerName(string playerName, string expectedName)
    {
        return NormalizePlayerName(playerName).Equals(NormalizePlayerName(expectedName), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePlayerName(string value)
    {
        return string.Concat(value
            .Normalize(NormalizationForm.FormD)
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark &&
                char.IsLetterOrDigit(character)))
            .ToLowerInvariant();
    }

    private static string CreateClubId(string leagueId, string clubName)
    {
        var normalizedClubName = string.Concat(clubName
            .Where(char.IsLetterOrDigit))
            .ToLowerInvariant();
        return $"{leagueId}:{normalizedClubName}";
    }

    private void AddMissingRosterListings(List<TransferPlayerListing> listings)
    {
        if (_state.SelectedTeam is null || _state.League is null)
        {
            return;
        }

        var listedIds = listings
            .Select(listing => listing.Player.PlayerId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var listedNames = listings
            .Select(listing => listing.Player.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var player in _state.SelectedTeam.Players.Concat(_state.SelectedTeam.Substitutes).Distinct())
        {
            if (!string.IsNullOrWhiteSpace(player.PlayerId) && listedIds.Contains(player.PlayerId) ||
                listedNames.Contains(player.Name))
            {
                continue;
            }

            PlayerContractService.EnsureContract(player, _state.League.LeagueId, GetSeasonEndYear(_state.League.Season));
            var marketValue = _marketValueCalculator.CalculateMarketValue(player, _state.League.LeagueId, _state.League.PlayerStats);
            listings.Add(new TransferPlayerListing(
                player,
                _state.SelectedTeam,
                _state.League.LeagueId,
                _state.League.Name,
                marketValue,
                _marketValueCalculator.CalculateAskingPrice(player, _state.League.LeagueId, _state.League.PlayerStats),
                "Available",
                player.WeeklyWage ?? PlayerContractService.EstimateWeeklyWage(player, _state.League.LeagueId),
                player.ContractEndYear,
                PlayerContractService.GetYearsRemaining(player, GetSeasonEndYear(_state.League.Season)),
                CreateContractStatusText(player)));
        }
    }

    private static string CreateContractStatusText(Player player)
    {
        return player.ContractStatus switch
        {
            PlayerContractStatus.ExpiringSoon => "Expiring Soon",
            PlayerContractStatus.PreContractEligible => "Pre-Contract Eligible",
            PlayerContractStatus.Expired => "Expired",
            PlayerContractStatus.FreeAgent => "Free Agent",
            _ => "Active"
        };
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

    private static int GetSeasonEndYear(string season)
    {
        if (string.IsNullOrWhiteSpace(season))
        {
            return DateTime.Now.Year + 1;
        }

        var startPart = season.Split('-', '/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return int.TryParse(startPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var startYear)
            ? startYear + 1
            : DateTime.Now.Year + 1;
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
