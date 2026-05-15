using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Wpf.Views;

public partial class LoadGameView : UserControl
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

    private readonly Action<SaveGameData, int> _loadGame;
    private readonly Action _goBack;
    private readonly SaveGameService _saveGameService = new();

    public LoadGameView(Action<SaveGameData, int> loadGame, Action goBack)
    {
        InitializeComponent();
        _loadGame = loadGame;
        _goBack = goBack;
        LoadSlots();
    }

    private void LoadSlots()
    {
        SaveSlotsItemsControl.ItemsSource = _saveGameService.GetSaveSlots()
            .Select(CreateSlotRow)
            .ToList();
    }

    private void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SaveSlotRow row })
        {
            return;
        }

        try
        {
            var saveData = _saveGameService.LoadGame(row.SlotNumber);
            if (saveData is null)
            {
                MessageBox.Show("This save slot is empty.", "Load Game", MessageBoxButton.OK, MessageBoxImage.Information);
                LoadSlots();
                return;
            }

            _loadGame(saveData, row.SlotNumber);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or NotSupportedException or IOException)
        {
            MessageBox.Show(
                $"This save file could not be loaded. You can delete it from the slot list.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Corrupted Save",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            LoadSlots();
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SaveSlotRow row })
        {
            return;
        }

        var result = MessageBox.Show(
            $"Delete save slot {row.SlotNumber}?",
            "Delete Save",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _saveGameService.DeleteSave(row.SlotNumber);
        LoadSlots();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _goBack();
    }

    private static SaveSlotRow CreateSlotRow(SaveGameSlotInfo slot)
    {
        if (slot.IsEmpty)
        {
            return new SaveSlotRow
            {
                SlotNumber = slot.SlotNumber,
                SlotTitle = $"Slot {slot.SlotNumber}",
                ClubName = "No saved game",
                RoundText = string.Empty,
                PositionText = string.Empty,
                PointsText = string.Empty,
                SavedAtText = string.Empty,
                EmptyVisibility = Visibility.Visible,
                LoadVisibility = Visibility.Collapsed,
                LogoVisibility = Visibility.Collapsed
            };
        }

        if (slot.IsCorrupted)
        {
            return new SaveSlotRow
            {
                SlotNumber = slot.SlotNumber,
                SlotTitle = $"Slot {slot.SlotNumber}",
                ClubName = "Save file corrupted",
                RoundText = "Unable to read save data",
                WarningText = "Delete this slot to reuse it.",
                DeleteVisibility = Visibility.Visible,
                LoadVisibility = Visibility.Collapsed,
                WarningVisibility = Visibility.Visible,
                LogoVisibility = Visibility.Collapsed
            };
        }

        return new SaveSlotRow
        {
            SlotNumber = slot.SlotNumber,
            SlotTitle = $"Slot {slot.SlotNumber}",
            ClubName = slot.SelectedClubName,
            LogoPath = GetClubLogoPath(slot.SelectedClubName),
            RoundText = $"Round {slot.CurrentRound}",
            PositionText = slot.LeaguePosition.HasValue ? $"Position #{slot.LeaguePosition}" : "Position -",
            PointsText = slot.Points.HasValue ? $"Points {slot.Points}" : "Points -",
            SavedAtText = slot.SavedAt.HasValue
                ? $"Saved: {slot.SavedAt.Value.ToString("dd MMM yyyy, h:mm tt", CultureInfo.CurrentCulture)}"
                : "Saved: -",
            CanLoad = true,
            DeleteVisibility = Visibility.Visible
        };
    }

    private static string GetClubLogoPath(string clubName)
    {
        foreach (var logoPath in GetLogoCandidatePaths(clubName))
        {
            if (ResourceExists(logoPath))
            {
                return logoPath;
            }
        }

        return string.Empty;
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

    private sealed class SaveSlotRow
    {
        public int SlotNumber { get; init; }
        public string SlotTitle { get; init; } = string.Empty;
        public string ClubName { get; init; } = string.Empty;
        public string LogoPath { get; init; } = string.Empty;
        public string RoundText { get; init; } = string.Empty;
        public string PositionText { get; init; } = string.Empty;
        public string PointsText { get; init; } = string.Empty;
        public string SavedAtText { get; init; } = string.Empty;
        public string WarningText { get; init; } = string.Empty;
        public bool CanLoad { get; init; }
        public Visibility LogoVisibility { get; init; } = Visibility.Visible;
        public Visibility LoadVisibility { get; init; } = Visibility.Visible;
        public Visibility DeleteVisibility { get; init; } = Visibility.Collapsed;
        public Visibility EmptyVisibility { get; init; } = Visibility.Collapsed;
        public Visibility WarningVisibility { get; init; } = Visibility.Collapsed;
    }
}
