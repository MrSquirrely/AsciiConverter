using System.Windows;
using System.Windows.Media;

namespace ASCIIV.Core.Controls;

public partial class CustomMessageBox {

    public enum MessageBoxType {
        Ok,           // Pale Green
        Confirmation, // Standard Dark
        Warning       // Pale Red
    }

    public CustomMessageBox(string message, string title, MessageBoxType type) {
        InitializeComponent();

        MessageText.Text = message;
        TitleText.Text = title;

        switch (type) {
            case MessageBoxType.Ok:
                SetLightAppearance("SuccessBackgroundBrush", "SuccessBorderBrush");
                break;

            case MessageBoxType.Warning:
                SetLightAppearance("WarningBackgroundBrush", "WarningBorderBrush");
                break;

            case MessageBoxType.Confirmation:
                // Keeps the dark window look defined in XAML, and asks a yes/no question
                CancelButton.Visibility = Visibility.Visible;
                OkButton.Content = "Yes";
                CancelButton.Content = "No";
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }
    }

    public static bool? Show(string message, string title = "Info", MessageBoxType type = MessageBoxType.Ok, Window? owner = null) {
        CustomMessageBox msg = new(message, title, type) { Owner = owner ?? Application.Current.MainWindow };
        return msg.ShowDialog();
    }

    // Pale background from the theme, with dark text so it stays readable
    private void SetLightAppearance(string backgroundKey, string borderKey) {
        MainBorder.Background = (Brush)FindResource(backgroundKey);
        MainBorder.BorderBrush = (Brush)FindResource(borderKey);

        Brush darkBrush = (Brush)FindResource("LightSurfaceTextBrush");
        TitleText.Foreground = darkBrush;
        MessageText.Foreground = darkBrush;
        CloseButton.Foreground = darkBrush;

        // The drop shadow under the text looks dirty on a flat light background
        MessageText.Effect = null;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) {
        DialogResult = true;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) {
        DialogResult = false;
        Close();
    }
}