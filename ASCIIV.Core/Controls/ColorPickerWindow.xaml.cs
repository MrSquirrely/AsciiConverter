using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ASCIIV.Core.Controls {
    public partial class ColorPickerWindow : Window {

        public Color SelectedColor { get; private set; } = Colors.White;

        public ColorPickerWindow() {
            InitializeComponent();
            UpdatePreview();
        }

        // Enable dragging the borderless window
        private void Window_MouseDown(object sender, MouseButtonEventArgs e) {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            if (RedSlider != null && GreenSlider != null && BlueSlider != null) {
                UpdatePreview();
            }
        }

        private void UpdatePreview() {
            byte r = (byte)RedSlider.Value;
            byte g = (byte)GreenSlider.Value;
            byte b = (byte)BlueSlider.Value;

            SelectedColor = Color.FromRgb(r, g, b);

            ColorPreviewBorder.Background = new SolidColorBrush(SelectedColor);

            RedValueText.Text = r.ToString();
            GreenValueText.Text = g.ToString();
            BlueValueText.Text = b.ToString();

            HexCodeText.Text = $"#{r:X2}{g:X2}{b:X2}";

            double brightness = (r * 0.299 + g * 0.587 + b * 0.114);
            HexCodeText.Foreground = brightness > 128 ? Brushes.Black : Brushes.White;
        }

        private void OkButton_Click(object sender, RoutedEventArgs e) {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) {
            DialogResult = false;
            Close();
        }
    }
}