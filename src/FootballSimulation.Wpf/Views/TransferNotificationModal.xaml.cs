using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace FootballSimulation.Wpf.Views;

public partial class TransferNotificationModal : UserControl
{
    public event EventHandler<TransferNotificationModalAction>? ActionRequested;

    public TransferNotificationModal()
    {
        InitializeComponent();
    }

    public void Show(TransferNotificationModalContext context)
    {
        TitleTextBlock.Text = context.Title;
        SubtitleTextBlock.Text = context.Subtitle;
        PlayerNameTextBlock.Text = context.PlayerName;
        PlayerMetaTextBlock.Text = context.PlayerMeta;
        StoryTextBlock.Text = context.StoryText;
        MessageTextBlock.Text = context.Message;
        HeaderBorder.Background = context.HeaderBrush;
        MessageBorder.Background = context.MessageBackground;
        MessageBorder.BorderBrush = context.MessageBorderBrush;
        MessageTextBlock.Foreground = context.MessageForeground;
        var playerImage = CreateImageSource(context.PlayerImagePath);
        PlayerImage.Source = playerImage;
        PlayerImage.Visibility = playerImage is null ? Visibility.Collapsed : Visibility.Visible;
        FallbackAvatar.Visibility = playerImage is null ? Visibility.Visible : Visibility.Collapsed;
        ClubLogoImage.Source = CreateImageSource(context.ClubLogoPath);

        SetDetail(DetailOnePanel, DetailOneLabelTextBlock, DetailOneValueTextBlock, context.DetailOneLabel, context.DetailOneValue);
        SetDetail(DetailTwoPanel, DetailTwoLabelTextBlock, DetailTwoValueTextBlock, context.DetailTwoLabel, context.DetailTwoValue);
        SetDetail(DetailThreePanel, DetailThreeLabelTextBlock, DetailThreeValueTextBlock, context.DetailThreeLabel, context.DetailThreeValue);
        SetDetail(DetailFourPanel, DetailFourLabelTextBlock, DetailFourValueTextBlock, context.DetailFourLabel, context.DetailFourValue);

        ConfigureButton(PrimaryActionButton, context.PrimaryButtonText);
        ConfigureButton(SecondaryActionButton, context.SecondaryButtonText);
        ConfigureButton(CancelActionButton, context.CancelButtonText);

        Visibility = Visibility.Visible;
        BeginEntranceAnimation();
    }

    public void Hide()
    {
        Visibility = Visibility.Collapsed;
        Opacity = 0;
    }

    private void BeginEntranceAnimation()
    {
        ModalScaleTransform.ScaleX = 0.96;
        ModalScaleTransform.ScaleY = 0.96;

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)));
        ModalScaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(190))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        ModalScaleTransform.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(190))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private static void SetDetail(StackPanel panel, TextBlock label, TextBlock value, string detailLabel, string detailValue)
    {
        var hasValue = !string.IsNullOrWhiteSpace(detailLabel) || !string.IsNullOrWhiteSpace(detailValue);
        panel.Visibility = hasValue ? Visibility.Visible : Visibility.Collapsed;
        label.Text = detailLabel;
        value.Text = detailValue;
    }

    private static void ConfigureButton(Button button, string label)
    {
        button.Visibility = string.IsNullOrWhiteSpace(label) ? Visibility.Collapsed : Visibility.Visible;
        button.Content = label;
    }

    private static BitmapImage? CreateImageSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return new BitmapImage(new Uri(path, UriKind.RelativeOrAbsolute));
        }
        catch
        {
            return null;
        }
    }

    private void PrimaryActionButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        ActionRequested?.Invoke(this, TransferNotificationModalAction.Primary);
    }

    private void SecondaryActionButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        ActionRequested?.Invoke(this, TransferNotificationModalAction.Secondary);
    }

    private void CancelActionButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        ActionRequested?.Invoke(this, TransferNotificationModalAction.Cancel);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        ActionRequested?.Invoke(this, TransferNotificationModalAction.Close);
    }
}

public enum TransferNotificationModalAction
{
    Primary,
    Secondary,
    Cancel,
    Close
}

public sealed record TransferNotificationModalContext(
    string Title,
    string Subtitle,
    string PlayerName,
    string PlayerMeta,
    string StoryText,
    string Message,
    string PlayerImagePath,
    string ClubLogoPath,
    string DetailOneLabel,
    string DetailOneValue,
    string DetailTwoLabel,
    string DetailTwoValue,
    string DetailThreeLabel,
    string DetailThreeValue,
    string DetailFourLabel,
    string DetailFourValue,
    string PrimaryButtonText,
    string SecondaryButtonText,
    string CancelButtonText,
    System.Windows.Media.Brush HeaderBrush,
    System.Windows.Media.Brush MessageBackground,
    System.Windows.Media.Brush MessageBorderBrush,
    System.Windows.Media.Brush MessageForeground);
