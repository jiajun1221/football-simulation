using System.Windows;
using System.Windows.Controls;
using FootballSimulation.Wpf.State;
using FootballSimulation.Wpf.Views;

namespace FootballSimulation.Wpf;

public partial class MainWindow : Window
{
    private readonly GameFlowState _state = new();

    public MainWindow()
    {
        InitializeComponent();
        ShowTeamSelection();
    }

    private void ShowTeamSelection()
    {
        Navigate(new TeamSelectionView(_state, Navigate));
    }

    private void Navigate(UserControl view)
    {
        MainContent.Content = view;
    }
}
