using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace FootballSimulation.Wpf.Controls;

public partial class FlagBadge : UserControl
{
    public static readonly DependencyProperty FlagSourceProperty = DependencyProperty.Register(
        nameof(FlagSource),
        typeof(string),
        typeof(FlagBadge),
        new PropertyMetadata(string.Empty, OnFlagSourceChanged));

    public FlagBadge()
    {
        InitializeComponent();
    }

    public string? FlagSource
    {
        get => (string?)GetValue(FlagSourceProperty);
        set => SetValue(FlagSourceProperty, value);
    }

    private static void OnFlagSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is FlagBadge badge)
        {
            badge.UpdateFlagSource(e.NewValue as string);
        }
    }

    private void UpdateFlagSource(string? flagSource)
    {
        if (string.IsNullOrWhiteSpace(flagSource))
        {
            ShowFallback();
            return;
        }

        try
        {
            FlagImage.Source = new BitmapImage(CreateResourceUri(flagSource));
            FlagImage.Visibility = Visibility.Visible;
            FallbackTextBlock.Visibility = Visibility.Collapsed;
        }
        catch (InvalidOperationException)
        {
            ShowFallback();
        }
        catch (UriFormatException)
        {
            ShowFallback();
        }
    }

    private static Uri CreateResourceUri(string flagSource)
    {
        var trimmed = flagSource.Trim();
        if (trimmed.StartsWith("pack://", StringComparison.OrdinalIgnoreCase) ||
            Uri.TryCreate(trimmed, UriKind.Absolute, out _))
        {
            return new Uri(trimmed, UriKind.Absolute);
        }

        return new Uri($"/{trimmed.TrimStart('/')}", UriKind.Relative);
    }

    private void FlagImage_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        ShowFallback();
    }

    private void ShowFallback()
    {
        FlagImage.Source = null;
        FlagImage.Visibility = Visibility.Collapsed;
        FallbackTextBlock.Visibility = Visibility.Visible;
    }
}
