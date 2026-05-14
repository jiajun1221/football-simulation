using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FootballSimulation.Models;

namespace FootballSimulation.Wpf.Views;

public partial class SaveSlotDialog : Window
{
    public int? SelectedSlotNumber { get; private set; }

    public SaveSlotDialog(IReadOnlyList<SaveGameSlotInfo> slots)
    {
        InitializeComponent();
        SaveSlotsItemsControl.ItemsSource = slots.Select(CreateSlotRow).ToList();
    }

    private void SelectSlotButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SaveSlotRow row })
        {
            return;
        }

        if (row.RequiresOverwriteConfirmation)
        {
            var result = MessageBox.Show(
                $"Overwrite save slot {row.SlotNumber}?",
                "Overwrite Save",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        SelectedSlotNumber = row.SlotNumber;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static SaveSlotRow CreateSlotRow(SaveGameSlotInfo slot)
    {
        if (slot.IsEmpty)
        {
            return new SaveSlotRow
            {
                SlotNumber = slot.SlotNumber,
                SlotTitle = $"Slot {slot.SlotNumber}",
                ClubName = "Empty Slot",
                SummaryText = "No saved game",
                ButtonText = "Save Here"
            };
        }

        if (slot.IsCorrupted)
        {
            return new SaveSlotRow
            {
                SlotNumber = slot.SlotNumber,
                SlotTitle = $"Slot {slot.SlotNumber}",
                ClubName = "Corrupted Save",
                SummaryText = "This slot can be overwritten.",
                ButtonText = "Overwrite",
                RequiresOverwriteConfirmation = true
            };
        }

        return new SaveSlotRow
        {
            SlotNumber = slot.SlotNumber,
            SlotTitle = $"Slot {slot.SlotNumber}",
            ClubName = slot.SelectedClubName,
            SummaryText = $"Round {slot.CurrentRound} | Position #{slot.LeaguePosition?.ToString(CultureInfo.CurrentCulture) ?? "-"} | Points {slot.Points?.ToString(CultureInfo.CurrentCulture) ?? "-"}",
            SavedAtText = slot.SavedAt.HasValue
                ? $"Saved: {slot.SavedAt.Value.ToString("dd MMM yyyy, h:mm tt", CultureInfo.CurrentCulture)}"
                : string.Empty,
            ButtonText = "Overwrite",
            RequiresOverwriteConfirmation = true
        };
    }

    private sealed class SaveSlotRow
    {
        public int SlotNumber { get; init; }
        public string SlotTitle { get; init; } = string.Empty;
        public string ClubName { get; init; } = string.Empty;
        public string SummaryText { get; init; } = string.Empty;
        public string SavedAtText { get; init; } = string.Empty;
        public string ButtonText { get; init; } = string.Empty;
        public bool RequiresOverwriteConfirmation { get; init; }
    }
}
