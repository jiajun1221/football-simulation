using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using FootballSimulation.Models;
using FootballSimulation.Services;

namespace FootballSimulation.Wpf.Controls;

public partial class TacticalSettingsPanel : UserControl
{
    public static readonly DependencyProperty PanelMaxHeightProperty = DependencyProperty.Register(
        nameof(PanelMaxHeight),
        typeof(double),
        typeof(TacticalSettingsPanel),
        new PropertyMetadata(360.0));

    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact),
        typeof(bool),
        typeof(TacticalSettingsPanel),
        new PropertyMetadata(false, OnCompactChanged));

    public static readonly DependencyProperty ShowCompactTitleProperty = DependencyProperty.Register(
        nameof(ShowCompactTitle),
        typeof(bool),
        typeof(TacticalSettingsPanel),
        new PropertyMetadata(true, OnCompactChanged));

    private readonly TacticalDimension[] _dimensions =
    [
        TacticalDimension.Mentality,
        TacticalDimension.Tempo,
        TacticalDimension.Width,
        TacticalDimension.PressingIntensity,
        TacticalDimension.DefensiveLine
    ];

    private TeamTactics _currentTactics = new();
    private string _currentFormation = "4-3-3 Holding";
    private int _selectedTabIndex;

    public TacticalSettingsPanel()
    {
        InitializeComponent();
        Refresh();
    }

    public event EventHandler? TacticsChanged;
    public event EventHandler<FormationSelectionChangedEventArgs>? FormationChanged;

    public double PanelMaxHeight
    {
        get => (double)GetValue(PanelMaxHeightProperty);
        set => SetValue(PanelMaxHeightProperty, value);
    }

    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }

    public bool ShowCompactTitle
    {
        get => (bool)GetValue(ShowCompactTitleProperty);
        set => SetValue(ShowCompactTitleProperty, value);
    }

    public void LoadTactics(TeamTactics tactics)
    {
        _currentTactics = TacticalProfileService.Clone(tactics);
        Refresh();
    }

    public void LoadFormation(string formation)
    {
        _currentFormation = FormationCatalogService.NormalizeFormationName(formation);
        RefreshFormations();
    }

    public void ApplyTo(TeamTactics tactics)
    {
        TacticalProfileService.CopyTo(_currentTactics, tactics);
    }

    public TeamTactics GetCurrentTactics()
    {
        return TacticalProfileService.Clone(_currentTactics);
    }

    private void Refresh()
    {
        ApplySummaryVisual();
        if (IsCompact)
        {
            SummaryTextBlock.Text = string.Empty;
        }
        else
        {
            SummaryTextBlock.Text = TacticalProfileService.CreateSummary(_currentTactics);
        }
        RefreshPresets();
        RefreshCards();
        RefreshFormations();
        ShowSelectedTab();
    }

    private void RefreshPresets()
    {
        PresetPanel.Children.Clear();
        foreach (var preset in TacticalProfileService.GetPresets())
        {
            var button = new Button
            {
                Content = preset.Label,
                ToolTip = preset.Description,
                FontSize = IsCompact ? 9 : 8.5,
                FontWeight = FontWeights.SemiBold,
                Padding = IsCompact ? new Thickness(8, 3, 8, 3) : new Thickness(6, 2, 6, 2),
                Margin = IsCompact ? new Thickness(0, 0, 6, 5) : new Thickness(0, 0, 4, 4),
                MinHeight = IsCompact ? 27 : 20,
                MinWidth = IsCompact ? 68 : 0,
                Style = (Style)Resources["TacticalPillButtonStyle"]
            };
            ApplyTacticalPresetVisual(button, preset, IsPresetSelected(preset));
            button.Click += (_, _) =>
            {
                _currentTactics = TacticalProfileService.Clone(preset.Tactics);
                NotifyTacticsChanged();
                _selectedTabIndex = 1;
                ShowSelectedTab();
            };

            PresetPanel.Children.Add(button);
        }
    }

    private void RefreshFormations()
    {
        FormationPanel.Children.Clear();
        foreach (var formation in FormationCatalogService.GetFormations())
        {
            var button = new Button
            {
                Content = formation.Name,
                ToolTip = formation.Category.ToString(),
                FontSize = IsCompact ? 9 : 8,
                Padding = IsCompact ? new Thickness(9, 3, 9, 3) : new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 6, 5),
                MinHeight = IsCompact ? 27 : 20,
                Style = (Style)Resources["TacticalPillButtonStyle"]
            };
            ApplyPresetButtonVisual(button, formation.Name == _currentFormation);
            button.Click += (_, _) =>
            {
                _currentFormation = formation.Name;
                RefreshFormations();
                FormationChanged?.Invoke(this, new FormationSelectionChangedEventArgs(_currentFormation));
            };
            FormationPanel.Children.Add(button);
        }
    }

    private void PresetTabButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedTabIndex = 0;
        ShowSelectedTab();
    }

    private void RangeTabButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedTabIndex = 1;
        ShowSelectedTab();
    }

    private void FormationTabButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedTabIndex = 2;
        ShowSelectedTab();
    }

    private void ShowSelectedTab()
    {
        PresetPanel.Visibility = _selectedTabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        CardsPanel.Visibility = _selectedTabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        FormationPanel.Visibility = _selectedTabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        ApplyPresetButtonVisual(PresetTabButton, _selectedTabIndex == 0);
        ApplyPresetButtonVisual(RangeTabButton, _selectedTabIndex == 1);
        ApplyPresetButtonVisual(FormationTabButton, _selectedTabIndex == 2);
    }

    private void RefreshCards()
    {
        CardsPanel.Children.Clear();

        foreach (var dimension in _dimensions)
        {
            CardsPanel.Children.Add(CreateCard(dimension));
        }
    }

    private UIElement CreateCard(TacticalDimension dimension)
    {
        if (IsCompact)
        {
            return CreateCompactRow(dimension);
        }

        var selected = TacticalProfileService.GetSelectedOption(_currentTactics, dimension);
        var border = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 6),
            Opacity = 0
        };
        border.SetResourceReference(Border.BackgroundProperty, "AppSecondaryCardBackground");
        border.SetResourceReference(Border.BorderBrushProperty, "AppBorderBrush");
        border.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));

        var stack = new StackPanel();

        var titleGrid = new Grid
        {
            VerticalAlignment = VerticalAlignment.Center
        };
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = CreateDimensionIcon(dimension);
        icon.Margin = new Thickness(0, 0, 6, 0);
        titleGrid.Children.Add(icon);

        var title = CreateText(GetDimensionTitle(dimension), 11, FontWeights.Bold, "AppTextBrush");
        Grid.SetColumn(title, 1);
        titleGrid.Children.Add(title);

        var label = new Border
        {
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(7, 1, 7, 1),
            VerticalAlignment = VerticalAlignment.Center
        };
        label.SetResourceReference(Border.BackgroundProperty, "AppAccentBrush");

        var labelText = new TextBlock
        {
            Text = selected.Label,
            Foreground = Brushes.White,
            FontSize = 8,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        label.Child = labelText;
        ApplyRangeBadgeVisual(label, labelText, selected);
        Grid.SetColumn(label, 2);
        titleGrid.Children.Add(label);

        stack.Children.Add(titleGrid);
        stack.Children.Add(new TextBlock
        {
            Text = GetCompactDescription(dimension, selected),
            FontSize = 8.5,
            LineHeight = 12,
            MaxHeight = double.PositiveInfinity,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 3, 0, 5)
        });
        ((TextBlock)stack.Children[^1]).SetResourceReference(TextBlock.ForegroundProperty, "AppMutedTextBrush");

        stack.Children.Add(CreateRangeSlider(
            dimension,
            TacticalProfileService.GetOptions(dimension),
            selected,
            option => ApplyRangeBadgeVisual(label, labelText, option)));

        border.Child = stack;
        return border;
    }

    private UIElement CreateCompactRow(TacticalDimension dimension)
    {
        var options = GetCompactOptions(dimension);
        var selected = GetSelectedCompactOption(dimension, options);
        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 0, 2),
            Opacity = 0
        };
        border.SetResourceReference(Border.BackgroundProperty, "AppSecondaryCardBackground");
        border.SetResourceReference(Border.BorderBrushProperty, "AppBorderBrush");
        border.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));

        var grid = new Grid
        {
            MinHeight = 22,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = CreateDimensionIcon(dimension);
        icon.Margin = new Thickness(0, 0, 5, 0);
        grid.Children.Add(icon);

        var title = CreateText(GetDimensionTitle(dimension), 8, FontWeights.Bold, "AppTextBrush");
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);

        var valueBadge = CreateSelectedBadge(selected.Label);
        var valueText = (TextBlock)valueBadge.Child;
        ApplyRangeBadgeVisual(valueBadge, valueText, selected);
        Grid.SetColumn(valueBadge, 3);
        grid.Children.Add(valueBadge);

        var slider = CreateRangeSlider(
            dimension,
            options,
            selected,
            option => ApplyRangeBadgeVisual(valueBadge, valueText, option));
        slider.Margin = new Thickness(5, 0, 5, 0);
        Grid.SetColumn(slider, 2);
        grid.Children.Add(slider);

        border.Child = grid;
        return border;
    }

    private Slider CreateRangeSlider(
        TacticalDimension dimension,
        IReadOnlyList<TacticalOption> options,
        TacticalOption selected,
        Action<TacticalOption> updateValueVisual)
    {
        var selectedIndex = options
            .Select((option, index) => new { option, index })
            .Where(item => item.option == selected)
            .Select(item => item.index)
            .DefaultIfEmpty(0)
            .First();
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = Math.Max(0, options.Count - 1),
            Value = selectedIndex,
            SmallChange = 1,
            LargeChange = 1,
            IsMoveToPointEnabled = true,
            ToolTip = selected.Description,
            Style = (Style)Resources["TacticalRangeSliderStyle"]
        };

        slider.ValueChanged += (_, args) =>
        {
            var optionIndex = Math.Clamp((int)Math.Round(args.NewValue), 0, options.Count - 1);
            var option = options[optionIndex];
            TacticalProfileService.ApplyOption(_currentTactics, option);
            slider.ToolTip = option.Description;
            updateValueVisual(option);
            RefreshSummaryAndPresets();
            TacticsChanged?.Invoke(this, EventArgs.Empty);
        };

        return slider;
    }

    private void RefreshSummaryAndPresets()
    {
        if (!IsCompact)
        {
            SummaryTextBlock.Text = TacticalProfileService.CreateSummary(_currentTactics);
        }

        RefreshPresets();
    }

    private static void ApplyRangeBadgeVisual(Border badge, TextBlock textBlock, TacticalOption option)
    {
        textBlock.Text = option.Label;
        var normalizedKey = option.Key.ToLowerInvariant();
        badge.Background = normalizedKey is "defensive" or "slow" or "narrow" or "low-block" or "deep"
            ? new SolidColorBrush(Color.FromRgb(37, 99, 235))
            : normalizedKey is "attacking" or "fast" or "wide" or "aggressive" or "higher"
                ? new SolidColorBrush(Color.FromRgb(239, 68, 68))
                : new SolidColorBrush(Color.FromRgb(100, 116, 139));
    }

    private Button CreateCompactDropdown(TacticalDimension dimension, TacticalOption selected, IReadOnlyList<TacticalOption> options)
    {
        var button = new Button
        {
            Content = selected.Label,
            MinWidth = 114,
            MaxWidth = 132,
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Style = (Style)Resources["CompactTacticDropdownButtonStyle"],
            Effect = CreateSelectedGlow(0.16, 6)
        };
        button.SetResourceReference(BackgroundProperty, "TacticalPresetActiveBackground");
        button.SetResourceReference(BorderBrushProperty, "TacticalPresetActiveBackground");

        var menu = new ContextMenu();
        foreach (var option in options)
        {
            var menuItem = new MenuItem
            {
                Header = option.Label,
                FontWeight = option == selected ? FontWeights.Bold : FontWeights.SemiBold,
                IsCheckable = true,
                IsChecked = option == selected
            };
            menuItem.Click += (_, _) =>
            {
                TacticalProfileService.ApplyOption(_currentTactics, option);
                NotifyTacticsChanged();
            };
            menu.Items.Add(menuItem);
        }

        button.ContextMenu = menu;
        button.Click += (_, _) =>
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        };

        return button;
    }

    private UIElement CreateScale(TacticalDimension dimension, TacticalOption selected, IReadOnlyList<TacticalOption>? optionsOverride = null)
    {
        var options = optionsOverride ?? TacticalProfileService.GetOptions(dimension);
        var grid = new UniformGrid
        {
            Columns = options.Count,
            Rows = 1
        };

        foreach (var option in options)
        {
            var isSelected = option == selected;
            var button = new Button
            {
                Content = IsCompact ? GetReadableCompactOptionLabel(option) : option.Label,
                ToolTip = option.Description,
                FontSize = IsCompact ? 7.2 : 8,
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.SemiBold,
                Padding = IsCompact ? new Thickness(3, 1, 3, 1) : new Thickness(3, 2, 3, 2),
                Margin = new Thickness(2, 0, 2, 0),
                MinHeight = IsCompact ? 19 : 20,
                Opacity = isSelected ? 1.0 : 0.84,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1),
                Style = (Style)Resources["TacticalPillButtonStyle"]
            };

            ApplyOptionButtonVisual(button, isSelected);
            if (isSelected)
            {
                AnimateSelectedButton(button);
            }
            button.Click += (_, _) =>
            {
                TacticalProfileService.ApplyOption(_currentTactics, option);
                NotifyTacticsChanged();
            };

            grid.Children.Add(button);
        }

        return grid;
    }

    private void ApplyPresetButtonVisual(Button button, bool isSelected)
    {
        ApplyHuePillVisual(button, Color.FromRgb(71, 85, 105), isSelected);
    }

    private static void ApplyTacticalPresetVisual(Button button, TacticalPreset preset, bool isSelected)
    {
        var color = preset.Tactics.Mentality switch
        {
            Mentality.UltraDefensive => Color.FromRgb(30, 58, 138),
            Mentality.Defensive => Color.FromRgb(37, 99, 235),
            Mentality.Attacking => Color.FromRgb(239, 68, 68),
            Mentality.AllOutAttack => Color.FromRgb(153, 27, 27),
            _ => Color.FromRgb(100, 116, 139)
        };
        ApplyHuePillVisual(button, color, isSelected);
    }

    private void ApplyOptionButtonVisual(Button button, bool isSelected)
    {
        ApplyHuePillVisual(button, Color.FromRgb(71, 85, 105), isSelected);
    }

    private static void ApplyHuePillVisual(Button button, Color hue, bool isSelected)
    {
        var state = button.Tag as PillVisualState;
        if (state is null)
        {
            state = new PillVisualState(button.Content?.ToString() ?? string.Empty);
            button.Tag = state;
            button.MouseEnter += PillButton_MouseEnter;
            button.MouseLeave += PillButton_MouseLeave;
        }

        state.Hue = hue;
        state.IsSelected = isSelected;
        button.Content = isSelected ? $"✓ {state.Label}" : state.Label;
        button.Background = new SolidColorBrush(isSelected ? Blend(hue, Colors.Black, 0.18) : Blend(hue, Colors.White, 0.82));
        button.Foreground = isSelected ? Brushes.White : new SolidColorBrush(Blend(hue, Colors.Black, 0.48));
        button.BorderBrush = new SolidColorBrush(isSelected ? Blend(hue, Colors.White, 0.34) : hue);
        button.BorderThickness = new Thickness(1);
        button.FontWeight = isSelected ? FontWeights.Black : FontWeights.SemiBold;
        button.Opacity = 1;
        button.Effect = isSelected
            ? new DropShadowEffect { Color = hue, BlurRadius = 7, ShadowDepth = 0, Opacity = 0.38 }
            : null;
    }

    private static void PillButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Button { Tag: PillVisualState { IsSelected: false } state } button)
        {
            button.Background = new SolidColorBrush(Blend(state.Hue, Colors.White, 0.68));
            button.Effect = new DropShadowEffect { Color = state.Hue, BlurRadius = 5, ShadowDepth = 0, Opacity = 0.22 };
        }
    }

    private static void PillButton_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is Button { Tag: PillVisualState { IsSelected: false } state } button)
        {
            button.Background = new SolidColorBrush(Blend(state.Hue, Colors.White, 0.82));
            button.Effect = null;
        }
    }

    private static Color Blend(Color source, Color target, double targetAmount)
    {
        return Color.FromRgb(
            (byte)Math.Round(source.R + ((target.R - source.R) * targetAmount)),
            (byte)Math.Round(source.G + ((target.G - source.G) * targetAmount)),
            (byte)Math.Round(source.B + ((target.B - source.B) * targetAmount)));
    }

    private static void AnimateSelectedButton(Button button)
    {
        if (button.RenderTransform is not ScaleTransform scale)
        {
            return;
        }

        var animation = new DoubleAnimation(1.0, 1.035, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    private static void OnCompactChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is TacticalSettingsPanel panel && panel.SummaryTextBlock is not null)
        {
            panel.Refresh();
        }
    }

    private bool IsPresetSelected(TacticalPreset preset)
    {
        return _currentTactics.Mentality == preset.Tactics.Mentality &&
            _currentTactics.PressingIntensity == preset.Tactics.PressingIntensity &&
            _currentTactics.Width == preset.Tactics.Width &&
            _currentTactics.Tempo == preset.Tactics.Tempo &&
            _currentTactics.DefensiveLine == preset.Tactics.DefensiveLine;
    }

    private void NotifyTacticsChanged()
    {
        Refresh();
        TacticsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplySummaryVisual()
    {
        CompactTitleTextBlock.Visibility = IsCompact && ShowCompactTitle ? Visibility.Visible : Visibility.Collapsed;
        SummaryTextBlock.Visibility = IsCompact ? Visibility.Collapsed : Visibility.Visible;
        SummaryTextBlock.FontSize = 10;
        SummaryTextBlock.LineHeight = 14;
        SummaryTextBlock.MaxHeight = double.PositiveInfinity;
        SummaryTextBlock.TextTrimming = IsCompact ? TextTrimming.CharacterEllipsis : TextTrimming.None;
        SummaryTextBlock.Margin = new Thickness(0, 0, 0, 7);
        PresetPanel.Margin = new Thickness(0, 0, 0, 6);
    }

    private static TextBlock CreateText(string text, double fontSize, FontWeight fontWeight, string brushResource)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = fontWeight,
            VerticalAlignment = VerticalAlignment.Center
        };
        textBlock.SetResourceReference(TextBlock.ForegroundProperty, brushResource);
        return textBlock;
    }

    private FrameworkElement CreateDimensionIcon(TacticalDimension dimension)
    {
        return CreateText(GetDimensionIcon(dimension), IsCompact ? 10.5 : 13, FontWeights.Bold, "AppAccentBrush");
    }

    private string GetDimensionTitle(TacticalDimension dimension)
    {
        return dimension switch
        {
            TacticalDimension.Mentality => "Mentality",
            TacticalDimension.PressingIntensity => IsCompact ? "Pressing" : "Pressing Intensity",
            TacticalDimension.Width => "Width",
            TacticalDimension.Tempo => "Tempo",
            TacticalDimension.DefensiveLine => "Defensive Line",
            _ => "Tactic"
        };
    }

    private static string GetDimensionIcon(TacticalDimension dimension)
    {
        return dimension switch
        {
            TacticalDimension.Mentality => "🧠",
            TacticalDimension.PressingIntensity => "⚔",
            TacticalDimension.Width => "↔",
            TacticalDimension.Tempo => "⚡",
            TacticalDimension.DefensiveLine => "🛡",
            _ => "•"
        };
    }

    private static string GetCompactDescription(TacticalDimension dimension, TacticalOption selected)
    {
        return dimension switch
        {
            TacticalDimension.Mentality => selected.Label == "Balanced" ? "Balanced attack and defense." : selected.Description,
            TacticalDimension.Tempo => "Controlled transition speed.",
            TacticalDimension.Width => "Horizontal attacking shape.",
            TacticalDimension.PressingIntensity => "Ball-winning pressure.",
            TacticalDimension.DefensiveLine => "Back-line height and risk.",
            _ => selected.Description
        };
    }

    private static string GetReadableCompactOptionLabel(TacticalOption option)
    {
        return option.Key switch
        {
            "defensive" => "Def",
            "balanced" => "Balanced",
            "attacking" => "Att",
            "slow" => "Slow",
            "fast" => "Fast",
            "narrow" => "Nar",
            "wide" => "Wide",
            "low-block" => "Low",
            "normal" => "Norm",
            "aggressive" => "Aggr",
            "deep" => "Deep",
            "standard" => "Std",
            "higher" => "High",
            _ => option.Label
        };
    }

    private static string GetCompactPresetLabel(TacticalPreset preset)
    {
        return preset.Key switch
        {
            "park-the-bus" => "Park",
            "tiki-taka" => "Tiki",
            "gegenpress" => "Press",
            "wing-play" => "Wing",
            "counter-attack" => "Cntr",
            "balanced" => "Bal",
            _ => preset.Label
        };
    }

    private sealed class PillVisualState(string label)
    {
        public string Label { get; } = label;
        public Color Hue { get; set; }
        public bool IsSelected { get; set; }
    }

    private static IReadOnlyList<TacticalOption> GetCompactOptions(TacticalDimension dimension)
    {
        return TacticalProfileService.GetOptions(dimension);
    }

    private TacticalOption GetSelectedCompactOption(TacticalDimension dimension, IReadOnlyList<TacticalOption> options)
    {
        if (dimension == TacticalDimension.Mentality)
        {
            var fullSelected = TacticalProfileService.GetSelectedOption(_currentTactics, dimension);
            return options.MinBy(option => Math.Abs(option.Value - fullSelected.Value)) ?? options[0];
        }

        var value = TacticalProfileService.GetValue(_currentTactics, dimension);
        return options.MinBy(option => Math.Abs(option.Value - value)) ?? options[0];
    }

    private static Border CreateSelectedBadge(string text)
    {
        var badge = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 1, 6, 1),
            VerticalAlignment = VerticalAlignment.Center
        };
        badge.SetResourceReference(Border.BackgroundProperty, "TacticalPresetActiveBackground");

        badge.Child = new TextBlock
        {
            Text = text,
            Foreground = Brushes.White,
            FontSize = 7.2,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        return badge;
    }

    private static DropShadowEffect CreateSelectedGlow(double opacity, double blurRadius)
    {
        return new DropShadowEffect
        {
            Color = Colors.DeepSkyBlue,
            BlurRadius = blurRadius,
            ShadowDepth = 0,
            Opacity = opacity
        };
    }
}

public sealed class FormationSelectionChangedEventArgs(string formation) : EventArgs
{
    public string Formation { get; } = formation;
}
