using ASCIIV.Core;
using ASCIIV.Core.Controls;
using ASCIIV.Player;

using Microsoft.Win32;

using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace ASCIIV.Converter;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow {
    private const string FFmpegFolderName = "ffmpeg";
    private const string CustomPresetName = "Custom";
    private const string VideoFileFilter =
        "Video Files|*.mp4;*.webm;*.gif;*.avi;*.mov;*.mkv|All Files|*.*";

    // The entries of the "Also Export" list, in the order they appear there
    private static readonly ExportFormat[] ExportChoices = [ExportFormat.None, ExportFormat.Mp4, ExportFormat.WebM, ExportFormat.Gif];

    // This string will hold the path to the user's video
    private string _inputVideoPath = string.Empty;

    // Stores the specific file created by the last conversion so "Play" works correctly
    private string _lastCreatedFilePath = string.Empty;

    // Default to White in case they select Custom but cancel the dialog
    private string _customColorHex = "#FFFFFF";

    // Allows cancelling long conversions. Not null only while a conversion is running
    private CancellationTokenSource? _conversionCts;

    // Set when the window is closed mid-conversion, so it closes once cleanup has finished
    private bool _closeAfterConversion;

    // The "Custom" entries of the preset lists, shown once a setting no longer matches a preset
    private readonly ComboBoxItem _customStyleItem = new() {
        Content = CustomPresetName,
        ToolTip = "Your own combination of settings"
    };

    private readonly ComboBoxItem _customSoundItem = new() {
        Content = CustomPresetName,
        ToolTip = "Your own audio settings"
    };

    // True while a preset is filling in the controls, so those changes are not mistaken for the user's
    private bool _isApplyingPreset;

    // False until the window has loaded, because controls raise change events while they are created
    private bool _isReady;

    public MainWindow() {
        InitializeComponent();

        foreach (StylePreset preset in (StylePreset[])[StylePreset.Default, .. StylePreset.All]) {
            PresetComboBox.Items.Add(new ComboBoxItem { Content = preset.Name, Tag = preset, ToolTip = preset.Description });
        }

        PresetComboBox.Items.Add(_customStyleItem);

        foreach (SoundPreset preset in SoundPreset.All) {
            SoundPresetComboBox.Items.Add(new ComboBoxItem { Content = preset.Name, Tag = preset, ToolTip = preset.Description });
        }

        SoundPresetComboBox.Items.Add(_customSoundItem);

        foreach (FrameFont font in FrameFont.All) {
            FontComboBox.Items.Add(new ComboBoxItem { Content = font.DisplayName, Tag = font });
        }

        ResetSettings();
    }

    // The stored name of the font chosen in the list; empty for the default font
    private string SelectedFontName => FontComboBox.SelectedItem is ComboBoxItem { Tag: FrameFont font } ? font.Name : string.Empty;

    // The colour chosen in the list, or the custom colour when nothing is selected there
    private string SelectedColorName => ColorComboBox.SelectedItem is ComboBoxItem item ? item.Content.ToString()! : _customColorHex;

    protected override void OnClosing(CancelEventArgs e) {
        if (_conversionCts is not null) {
            // Cancel and wait for the conversion to clean up its temp files before closing
            _closeAfterConversion = true;
            _conversionCts.Cancel();
            CancelButton.IsEnabled = false;
            StatusText.Text = "Cancelling conversion...";
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    // --- STARTUP ---

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e) {
        _isReady = true;

        bool hasFFmpeg = await InitializeFFmpeg();
        LoadButton.IsEnabled = true;

        if (hasFFmpeg) {
            StatusText.Text = "Ready";
            return;
        }

        StatusText.Text = "Ready (FFmpeg unavailable)";
        CustomMessageBox.Show(
            "FFmpeg could not be found or downloaded.\n\n"
            + "Videos can still be converted, but without audio, and exporting to MP4, WebM or GIF will not work.\n\n"
            + "Check your internet connection and restart the application to try again.",
            "FFmpeg Unavailable",
            CustomMessageBox.MessageBoxType.Warning,
            this);
    }

    // Uses an FFmpeg next to the application if there is one, otherwise downloads it once per user
    private async Task<bool> InitializeFFmpeg() {
        string bundledFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FFmpegFolderName);
        string downloadFolder = Path.Combine(AppPaths.AppDataFolder, FFmpegFolderName);

        foreach (string folder in (string[])[bundledFolder, downloadFolder]) {
            if (HasFFmpeg(folder)) {
                FFmpeg.SetExecutablesPath(folder);
                return true;
            }
        }

        try {
            StatusText.Text = "Downloading FFmpeg (first run only)...";
            Directory.CreateDirectory(downloadFolder);
            await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, downloadFolder);
        }
        catch (Exception ex) {
            Debug.WriteLine("FFmpeg download failed: " + ex.Message);
            return false;
        }

        FFmpeg.SetExecutablesPath(downloadFolder);
        return HasFFmpeg(downloadFolder);
    }

    // Xabe.FFmpeg needs both tools: ffprobe reads media info, ffmpeg converts
    private static bool HasFFmpeg(string folder) {
        return File.Exists(Path.Combine(folder, "ffmpeg.exe")) && File.Exists(Path.Combine(folder, "ffprobe.exe"));
    }

    // --- SETTINGS ---

    private void LoadButton_Click(object sender, RoutedEventArgs e) {
        OpenFileDialog openFileDialog = new() {
            Filter = VideoFileFilter
        };

        if (openFileDialog.ShowDialog() != true) {
            return;
        }

        _inputVideoPath = openFileDialog.FileName;
        StatusText.Text = "Video loaded: " + Path.GetFileName(_inputVideoPath);
        ProjectNameText.Text = Path.GetFileNameWithoutExtension(_inputVideoPath);
        ConvertButton.IsEnabled = true;
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e) {
        ResetSettings();
        StatusText.Text = "Settings reset.";
    }

    // Puts every output setting back to its starting value. The loaded video and project name are kept
    private void ResetSettings() {
        // Forget the custom colour and show the picker button in its normal colours again
        _customColorHex = "#FFFFFF";
        if (CustomColorButton.Template?.FindName("butonBorder", CustomColorButton) is Border swatch) {
            swatch.ClearValue(Border.BackgroundProperty);
        }

        ApplyStylePreset(StylePreset.Default);
        ExportComboBox.SelectedIndex = 0;
    }

    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (!_isApplyingPreset && (PresetComboBox.SelectedItem is ComboBoxItem { Tag: StylePreset preset })) {
            ApplyStylePreset(preset);
        }
    }

    private void SoundPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (!_isApplyingPreset && (SoundPresetComboBox.SelectedItem is ComboBoxItem { Tag: SoundPreset preset })) {
            ApplySoundPreset(preset);
            // The sound no longer belongs to the chosen style preset
            MarkStyleAsCustom();
        }
    }

    // Fills in every look and sound setting, and shows the preset as selected in both lists
    private void ApplyStylePreset(StylePreset preset) {
        _isApplyingPreset = true;
        try {
            TextStyleComboBox.Text = preset.TextStyle;
            SelectPresetItem(FontComboBox, FrameFont.Get(preset.FontName), (ComboBoxItem)FontComboBox.Items[0]);
            SelectColor(preset.ColorName);
            MatchColorsCheck.IsChecked = preset.MatchColors;
            VideoWidthSlider.Value = preset.Width;
            ContrastSlider.Value = preset.Contrast;
            InvertCheck.IsChecked = preset.Invert;
            SelectPresetItem(PresetComboBox, preset, _customStyleItem);
        }
        finally {
            _isApplyingPreset = false;
        }

        ApplySoundPreset(preset.Sound);
    }

    private void ApplySoundPreset(SoundPreset preset) {
        _isApplyingPreset = true;
        try {
            BitCrushCheck.IsChecked = preset.BitCrush;
            BitDepthSlider.Value = preset.BitDepth;
            DownsampleSlider.Value = preset.DownsampleFactor;
            SelectPresetItem(SoundPresetComboBox, preset, _customSoundItem);
        }
        finally {
            _isApplyingPreset = false;
        }
    }

    // Selects the list entry that carries the preset, or the "Custom" entry when there is none
    private static void SelectPresetItem(ComboBox comboBox, object preset, ComboBoxItem customItem) {
        foreach (ComboBoxItem item in comboBox.Items) {
            if (Equals(item.Tag, preset)) {
                comboBox.SelectedItem = item;
                return;
            }
        }

        comboBox.SelectedItem = customItem;
    }

    private void FontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        PreviewView.FontName = SelectedFontName;
        MarkStyleAsCustom();
    }

    // The user changed a look setting by hand
    private void StyleSetting_Changed(object sender, RoutedEventArgs e) {
        MarkStyleAsCustom();
    }

    // The user changed an audio setting by hand
    private void SoundSetting_Changed(object sender, RoutedEventArgs e) {
        if (!_isReady || _isApplyingPreset) {
            return;
        }

        _isApplyingPreset = true;
        try {
            SoundPresetComboBox.SelectedItem = _customSoundItem;
        }
        finally {
            _isApplyingPreset = false;
        }

        MarkStyleAsCustom();
    }

    private void MarkStyleAsCustom() {
        if (!_isReady || _isApplyingPreset) {
            return;
        }

        _isApplyingPreset = true;
        try {
            PresetComboBox.SelectedItem = _customStyleItem;
        }
        finally {
            _isApplyingPreset = false;
        }
    }

    private void MatchColorsCheck_Changed(object sender, RoutedEventArgs e) {
        // A single text colour means nothing while every character takes its colour from the video
        ColorPanel.IsEnabled = MatchColorsCheck.IsChecked != true;
        MarkStyleAsCustom();
    }

    private void CustomColorButton_OnClick(object sender, RoutedEventArgs e) {
        ColorPickerWindow picker = new() { Owner = this };
        if (picker.ShowDialog() == true) {
            SetCustomColor(picker.SelectedColor);
            MarkStyleAsCustom();
        }
    }

    // Selects a named colour from the list, or shows any other colour as the custom colour
    private void SelectColor(string colorName) {
        foreach (ComboBoxItem item in ColorComboBox.Items) {
            if (string.Equals(item.Content.ToString(), colorName, StringComparison.OrdinalIgnoreCase)) {
                ColorComboBox.SelectedItem = item;
                PreviewView.Foreground = ParseBrush(colorName);
                return;
            }
        }

        if (ParseBrush(colorName) is SolidColorBrush brush) {
            SetCustomColor(brush.Color);
        }
    }

    private void SetCustomColor(Color color) {
        _customColorHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        SolidColorBrush brush = new(color);
        if (CustomColorButton.Template.FindName("butonBorder", CustomColorButton) is Border swatch) {
            swatch.Background = brush;
        }

        ColorComboBox.SelectedIndex = -1;
        PreviewView.Foreground = brush;
    }

    private static Brush ParseBrush(string colorName) {
        try {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorName));
        }
        catch (FormatException) {
            return Brushes.White;
        }
    }

    // --- CONVERSION ---

    private async void ConvertButton_Click(object sender, RoutedEventArgs e) {
        if (string.IsNullOrEmpty(_inputVideoPath)) {
            return;
        }

        // Capture the UI state here, on the UI thread
        string selectedColor = SelectedColorName;
        ConversionOptions options = new(
            VideoPath: _inputVideoPath,
            OutputFolder: AppPaths.OutputFolder,
            WorkFolder: AppPaths.WorkFolder,
            ProjectName: ProjectSanitizer.SanitizeProjectName(ProjectNameText.Text),
            Ramp: AsciiRamps.Resolve(TextStyleComboBox.Text),
            Width: (int)VideoWidthSlider.Value,
            Contrast: ContrastSlider.Value,
            Invert: InvertCheck.IsChecked == true,
            ColorName: selectedColor,
            MatchColors: MatchColorsCheck.IsChecked == true,
            BitCrush: BitCrushCheck.IsChecked == true,
            BitDepth: (int)BitDepthSlider.Value,
            DownsampleFactor: (int)DownsampleSlider.Value,
            Export: ExportChoices[Math.Clamp(ExportComboBox.SelectedIndex, 0, ExportChoices.Length - 1)],
            FontName: SelectedFontName);

        // Created here, on the UI thread, so the callbacks are marshalled back to it
        ConversionProgress progress = new(
            Percent: new Progress<double>(ShowProgress),
            Status: new Progress<string>(text => StatusText.Text = text),
            Preview: new Progress<AsciiFrame>(ShowPreview));

        _conversionCts = new CancellationTokenSource();
        CancellationToken token = _conversionCts.Token;

        ConvertButton.IsEnabled = false;
        LoadButton.IsEnabled = false;
        CancelButton.Visibility = Visibility.Visible;
        CancelButton.IsEnabled = true;
        StatusText.Text = "Converting... Please wait.";
        ConvertProgressBar.Value = 0;
        PreviewView.Foreground = ParseBrush(selectedColor);

        ConversionResult? result = null;
        try {
            result = await Task.Run(() => VideoConverter.ConvertAsync(options, progress, token), token);

            _lastCreatedFilePath = result.AsciivPath;
            ConvertProgressBar.Value = 100;
            StatusText.Text = "Conversion Complete!";
        }
        catch (OperationCanceledException) {
            StatusText.Text = "Conversion Cancelled.";
            ConvertProgressBar.Value = 0;
        }
        catch (Exception ex) {
            Debug.WriteLine(ex);
            StatusText.Text = "Error: " + ex.Message;
            ConvertProgressBar.Value = 0;
        }
        finally {
            ConvertButton.IsEnabled = true;
            LoadButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            CancelButton.Visibility = Visibility.Collapsed;
            ConvertProgressBar.IsIndeterminate = false;
            _conversionCts.Dispose();
            _conversionCts = null;
        }

        if (_closeAfterConversion) {
            Close();
            return;
        }

        if (result is not null) {
            ShowCompletionMessage(result);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) {
        _conversionCts?.Cancel();
        CancelButton.IsEnabled = false;
        StatusText.Text = "Cancelling conversion...";
    }

    private void ShowProgress(double percent) {
        ConvertProgressBar.IsIndeterminate = percent < 0;
        if (percent >= 0) {
            ConvertProgressBar.Value = Math.Clamp(percent, 0, 100);
        }
    }

    private void ShowPreview(AsciiFrame frame) {
        PreviewPlaceholder.Visibility = Visibility.Collapsed;
        PreviewView.Frame = frame;
    }

    private void ShowCompletionMessage(ConversionResult result) {
        string message = "Conversion Complete! Saved:\n" + result.AsciivPath;
        if (result.ExportPath is not null) {
            message += "\n" + result.ExportPath;
        }

        if (result.AudioWarning is not null) {
            message += "\n\nNote: " + result.AudioWarning;
        }

        CustomMessageBox.Show(message, "Success", CustomMessageBox.MessageBoxType.Ok, this);
    }

    // --- OUTPUT ---

    private void PlayButton_Click(object sender, RoutedEventArgs e) {
        // Prefer the file created by the most recent conversion
        string path = _lastCreatedFilePath;

        // Fallback: a file named after the text box input
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
            // Sanitized so the name cannot point outside the output folder
            string safeName = ProjectSanitizer.SanitizeProjectName(ProjectNameText.Text, string.Empty);
            path = string.IsNullOrEmpty(safeName)
                ? string.Empty
                : Path.Combine(AppPaths.OutputFolder, safeName + AsciivFormat.FileExtension);
        }

        if (string.IsNullOrEmpty(path) || !File.Exists(path)) {
            CustomMessageBox.Show(
                "Please convert a video first or ensure the Project Name matches an existing file in:\n" + AppPaths.OutputFolder,
                "Nothing to Play",
                CustomMessageBox.MessageBoxType.Warning,
                this);
            return;
        }

        PlayerWindow player = new(path);
        player.Show();
    }

    private void OpenOutputFolderButton_Click(object sender, RoutedEventArgs e) {
        try {
            // The folder does not exist until the first conversion, so make sure there is something to show
            Directory.CreateDirectory(AppPaths.OutputFolder);
            using Process? explorer = Process.Start(new ProcessStartInfo(AppPaths.OutputFolder) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception) {
            CustomMessageBox.Show(
                "The output folder could not be opened:\n" + AppPaths.OutputFolder + "\n\n" + ex.Message,
                "Error",
                CustomMessageBox.MessageBoxType.Warning,
                this);
        }
    }

    // --- WINDOW ---

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton == MouseButton.Left) {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) {
        Close();
    }
}