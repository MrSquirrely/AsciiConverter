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

            TxtMessage.Text = message;
            TxtTitle.Text = title;

            switch (type) {
                case MessageBoxType.Ok:
                    // 1. Pale Green Background
                    MainBorder.Background = new SolidColorBrush(Color.FromRgb(212, 237, 218));
                    MainBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(195, 230, 203));

                    // 2. Set Text to Dark
                    SetLightAppearance();

                    BtnOk.Visibility = Visibility.Visible;
                    BtnCancel.Visibility = Visibility.Collapsed;
                    BtnOk.Content = "OK";
                    break;

                case MessageBoxType.Warning:
                    // 1. Pale Red Background
                    MainBorder.Background = new SolidColorBrush(Color.FromRgb(248, 215, 218));
                    MainBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 198, 203));

                    // 2. Set Text to Dark
                    SetLightAppearance();

                    BtnOk.Visibility = Visibility.Visible;
                    BtnCancel.Visibility = Visibility.Collapsed;
                    BtnOk.Content = "OK";
                    break;

                case MessageBoxType.Confirmation:
                    // Keep the default Dark Gradient (defined in XAML)
                    // Text is already White by default

                    BtnOk.Visibility = Visibility.Visible;
                    BtnCancel.Visibility = Visibility.Visible;
                    BtnOk.Content = "Yes";
                    BtnCancel.Content = "No";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        private void SetLightAppearance() {
            // Change text color to dark gray for readability on light backgrounds
            SolidColorBrush darkBrush = new SolidColorBrush(Color.FromRgb(30, 30, 30));

            TxtTitle.Foreground = darkBrush;
            TxtMessage.Foreground = darkBrush;
            BtnClose.Foreground = darkBrush;

            // Remove the drop shadow effect from text (looks cleaner on flat light bg)
            TxtTitle.Effect = null;
            TxtMessage.Effect = null;
        }

        public static bool? Show(string message, string title = "Info", MessageBoxType type = MessageBoxType.Ok, Window? owner = null) {
            CustomMessageBox msg = new CustomMessageBox(message, title, type);
            msg.Owner = owner ?? Application.Current.MainWindow;
            return msg.ShowDialog();
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e) {
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) {
            DialogResult = false;
            Close();
        }
    }
}