using System.Windows;
using System.Windows.Media;

namespace AsciiConverter {
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
                    // 1. Pale Green Background
                    MainBorder.Background = new SolidColorBrush(Color.FromRgb(212, 237, 218));
                    MainBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(195, 230, 203));

                    // 2. Set Text to Dark
                    SetLightAppearance();

                    OkButton.Visibility = Visibility.Visible;
                    CancelButton.Visibility = Visibility.Collapsed;
                    OkButton.Content = "OK";
                    break;

                case MessageBoxType.Warning:
                    // 1. Pale Red Background
                    MainBorder.Background = new SolidColorBrush(Color.FromRgb(248, 215, 218));
                    MainBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 198, 203));

                    // 2. Set Text to Dark
                    SetLightAppearance();

                    OkButton.Visibility = Visibility.Visible;
                    CancelButton.Visibility = Visibility.Collapsed;
                    OkButton.Content = "OK";
                    break;

                case MessageBoxType.Confirmation:
                    // Keep the default Dark Gradient (defined in XAML)
                    // Text is already White by default

                    OkButton.Visibility = Visibility.Visible;
                    CancelButton.Visibility = Visibility.Visible;
                    OkButton.Content = "Yes";
                    CancelButton.Content = "No";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        private void SetLightAppearance() {
            // Change text color to dark gray for readability on light backgrounds
            SolidColorBrush darkBrush = new SolidColorBrush(Color.FromRgb(30, 30, 30));

            TitleText.Foreground = darkBrush;
            MessageText.Foreground = darkBrush;
            CloseButton.Foreground = darkBrush;

            // Remove the drop shadow effect from text (looks cleaner on flat light bg)
            TitleText.Effect = null;
            MessageText.Effect = null;
        }

        public static bool? Show(string message, string title = "Info", MessageBoxType type = MessageBoxType.Ok, Window? owner = null) {
            CustomMessageBox msg = new(message, title, type) { Owner = owner ?? Application.Current.MainWindow };
            return msg.ShowDialog();
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
}