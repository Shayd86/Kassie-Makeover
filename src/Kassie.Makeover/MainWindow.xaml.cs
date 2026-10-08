using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kassie.Makeover.Infrastructure;
using Kassie.Makeover.Models;
using Kassie.Makeover.Services;

namespace Kassie.Makeover;

public partial class MainWindow : Window
{
    private readonly CameraService _camera = new();
    private WriteableBitmap? _previewBitmap;
    private bool _closing;

    public MainWindow()
    {
        InitializeComponent();
        _camera.FrameReady += Camera_FrameReady;
        _camera.StatusChanged += (_, status) => Dispatcher.BeginInvoke(() => SetCameraStatus(status));
        _camera.Error += (_, message) => Dispatcher.BeginInvoke(() => ShowCameraError(message));
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshCamerasAsync();
    }

    private async Task RefreshCamerasAsync()
    {
        GlobalStatus.Text = "Scanning cameras…";
        CameraCombo.IsEnabled = false;
        StartCameraButton.IsEnabled = false;
        try
        {
            var devices = await _camera.EnumerateAsync();
            CameraCombo.ItemsSource = devices;
            if (devices.Count > 0)
            {
                CameraCombo.SelectedIndex = 0;
                SetCameraStatus($"Found {devices.Count} camera{(devices.Count == 1 ? "" : "s")}");
                StartCameraButton.IsEnabled = true;
            }
            else
            {
                SetCameraStatus("No camera could be opened. Check Windows camera privacy settings and D:\\Kassie\\Makeover\\logs\\camera.log.");
            }
        }
        catch (Exception ex)
        {
            AppLog.Camera($"Enumeration error: {ex}");
            ShowCameraError($"Camera scan failed: {ex.Message}");
        }
        finally
        {
            CameraCombo.IsEnabled = true;
            GlobalStatus.Text = "Native C# ready";
        }
    }

    private async Task StartSelectedCameraAsync()
    {
        if (CameraCombo.SelectedItem is not CameraDevice device)
        {
            SetCameraStatus("Choose a camera first.");
            return;
        }

        StartCameraButton.IsEnabled = false;
        try
        {
            _camera.Mirror = MirrorCheck.IsChecked == true;
            await _camera.StartAsync(device.Index);
            CameraPlaceholder.Visibility = Visibility.Collapsed;
            StartCameraButton.Content = "Restart camera";
        }
        catch (Exception ex)
        {
            AppLog.Camera($"Start UI error: {ex}");
            ShowCameraError(ex.Message);
        }
        finally
        {
            StartCameraButton.IsEnabled = true;
        }
    }

    private void Camera_FrameReady(object? sender, CameraFrameEventArgs e)
    {
        if (_closing) return;
        try
        {
            Dispatcher.Invoke(() =>
            {
                if (_previewBitmap is null || _previewBitmap.PixelWidth != e.Width || _previewBitmap.PixelHeight != e.Height)
                {
                    _previewBitmap = new WriteableBitmap(e.Width, e.Height, 96, 96, PixelFormats.Bgra32, null);
                    PreviewImage.Source = _previewBitmap;
                }

                _previewBitmap.WritePixels(new Int32Rect(0, 0, e.Width, e.Height), e.Data, e.BufferSize, e.Stride);
                CameraPlaceholder.Visibility = Visibility.Collapsed;
            });
        }
        catch (TaskCanceledException) { }
        catch (Exception ex)
        {
            AppLog.Camera($"Preview update error: {ex.Message}");
        }
    }

    private void SetCameraStatus(string text)
    {
        CameraStatusText.Text = text;
        GlobalStatus.Text = text.StartsWith("Camera live", StringComparison.OrdinalIgnoreCase) ? "Camera live" : "Native C# ready";
    }

    private void ShowCameraError(string message)
    {
        SetCameraStatus(message);
        CameraPlaceholder.Visibility = Visibility.Visible;
    }

    private async void StartCamera_Click(object sender, RoutedEventArgs e) => await StartSelectedCameraAsync();

    private async void RefreshCameras_Click(object sender, RoutedEventArgs e) => await RefreshCamerasAsync();

    private async void CameraCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || CameraCombo.SelectedItem is null || !_camera.IsRunning) return;
        await StartSelectedCameraAsync();
    }

    private void MirrorCheck_Changed(object sender, RoutedEventArgs e)
    {
        _camera.Mirror = MirrorCheck.IsChecked == true;
    }

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _camera.SaveSnapshot();
            SetCameraStatus($"Saved {System.IO.Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            SetCameraStatus(ex.Message);
        }
    }

    private void OpenOutputs_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.Ensure();
        Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.Outputs) { UseShellExecute = true });
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string page) return;
        PageTitle.Text = page;

        if (page == "Live Mirror")
        {
            PageSubtitle.Text = "Native webcam preview with clean shutdown and capture.";
            LivePage.Visibility = Visibility.Visible;
            PlaceholderPage.Visibility = Visibility.Collapsed;
            return;
        }

        LivePage.Visibility = Visibility.Collapsed;
        PlaceholderPage.Visibility = Visibility.Visible;
        PlaceholderTitle.Text = page;
        (PlaceholderIcon.Text, PlaceholderText.Text, PageSubtitle.Text) = page switch
        {
            "Makeup" => ("✦", "Live face landmarks, makeup layers and adjustable intensity will plug into this native rendering surface next.", "Real-time makeup guidance and preview."),
            "Hair" => ("⌁", "Hair segmentation, colour preview and higher-quality AI hairstyle renders will live here.", "Colour, cut and hairstyle previews."),
            "Outfit" => ("♢", "Both live webcam outfit preview and higher-quality still-image try-on remain part of the product plan.", "Live and photo outfit try-on."),
            "Looks" => ("♥", "Saved combinations of makeup, hair and outfits will be managed here.", "Build and compare complete looks."),
            "Wardrobe" => ("▦", "Your own garments will be stored under D:\\Kassie\\Makeover\\wardrobe, independent of app updates.", "Your saved clothing library."),
            _ => ("✦", "This section is ready for the native C# rendering pipeline.", "Kassie Makeover")
        };
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            ToggleMaximize();
        else if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _closing = true;
        try { _camera.Dispose(); } catch { }
    }
}
