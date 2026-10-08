using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using OpenCvSharp;
using Kassie.Makeover.Infrastructure;
using Kassie.Makeover.Models;
using Kassie.Makeover.Services;

namespace Kassie.Makeover;

public partial class MainWindow : System.Windows.Window
{
    private sealed record ColorChoice(string Name, string Hex)
    {
        public override string ToString() => Name;
    }

    private static readonly ColorChoice[] LipColors =
    [
        new("Rose", "#D84D72"),
        new("Berry", "#9D3158"),
        new("Classic Red", "#D5283E"),
        new("Coral", "#E86F61"),
        new("Nude Pink", "#B96F72"),
        new("Plum", "#773B67")
    ];

    private static readonly ColorChoice[] BlushColors =
    [
        new("Soft Rose", "#E6758E"),
        new("Peach", "#E89472"),
        new("Pink", "#F06E9C"),
        new("Mauve", "#B96686"),
        new("Warm Nude", "#C77B68")
    ];

    private static readonly ColorChoice[] EyeColors =
    [
        new("Mauve", "#8A5A7A"),
        new("Bronze", "#9B6A43"),
        new("Soft Brown", "#71544A"),
        new("Rose", "#A95D73"),
        new("Smoky Grey", "#5F6070"),
        new("Blue", "#586FA8")
    ];

    private readonly CameraService _camera = new();
    private readonly ModelAssetService _modelAssets = new();
    private readonly MakeupService _makeup = new();
    private readonly LookPresetService _lookService = new();
    private readonly WardrobeService _wardrobeService = new();

    private WriteableBitmap? _previewBitmap;
    private MakeupSettings _makeupSettings = new();
    private List<LookPreset> _looks = [];
    private List<WardrobeItem> _wardrobe = [];
    private string _activePage = "Live Mirror";
    private bool _closing;
    private bool _uiReady;
    private bool _makeupLoading;

    public MainWindow()
    {
        InitializeComponent();

        LipColorCombo.ItemsSource = LipColors;
        BlushColorCombo.ItemsSource = BlushColors;
        EyeColorCombo.ItemsSource = EyeColors;
        LipColorCombo.SelectedIndex = 0;
        BlushColorCombo.SelectedIndex = 0;
        EyeColorCombo.SelectedIndex = 0;

        _camera.FrameProcessor = ProcessFrame;
        _camera.FrameReady += Camera_FrameReady;
        _camera.StatusChanged += (_, status) => Dispatcher.BeginInvoke(() => SetCameraStatus(status));
        _camera.Error += (_, message) => Dispatcher.BeginInvoke(() => ShowCameraError(message));

        Loaded += MainWindow_Loaded;
        _uiReady = true;
        UpdateMakeupSettings();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Kassie Makeover  •  v{version?.Major}.{version?.Minor}.{version?.Build}  •  C# / WPF";

        _looks = _lookService.Load();
        RefreshLooks();

        _wardrobe = _wardrobeService.Load();
        RefreshWardrobe();

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
            await RefreshCamerasAsync();
            if (CameraCombo.SelectedItem is not CameraDevice refreshed)
            {
                SetCameraStatus("Choose a camera first.");
                return;
            }
            device = refreshed;
        }

        StartCameraButton.IsEnabled = false;
        try
        {
            _camera.Mirror = MirrorCheck.IsChecked == true;
            await _camera.StartAsync(device.Index);
            CameraPlaceholder.Visibility = Visibility.Collapsed;
            MakeupCameraPlaceholder.Visibility = Visibility.Collapsed;
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

    private void ProcessFrame(Mat frame)
    {
        var settings = _makeupSettings;
        if (_activePage == "Makeup" && settings.Enabled && _makeup.Ready)
            _makeup.Apply(frame, settings);
    }

    private void Camera_FrameReady(object? sender, CameraFrameEventArgs e)
    {
        if (_closing) return;
        try
        {
            Dispatcher.Invoke(() =>
            {
                if (_previewBitmap is null || _previewBitmap.PixelWidth != e.Width || _previewBitmap.PixelHeight != e.Height)
                    _previewBitmap = new WriteableBitmap(e.Width, e.Height, 96, 96, PixelFormats.Bgra32, null);

                _previewBitmap.WritePixels(new Int32Rect(0, 0, e.Width, e.Height), e.Data, e.BufferSize, e.Stride);
                PreviewImage.Source = _previewBitmap;
                MakeupPreviewImage.Source = _previewBitmap;
                CameraPlaceholder.Visibility = Visibility.Collapsed;
                MakeupCameraPlaceholder.Visibility = Visibility.Collapsed;
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
        MakeupCameraPlaceholder.Visibility = Visibility.Visible;
    }

    private async Task EnsureMakeupReadyAsync()
    {
        if (_makeup.Ready || _makeupLoading) return;

        _makeupLoading = true;
        LiveMakeupCheck.IsEnabled = false;
        try
        {
            var progress = new Progress<string>(message => MakeupModelStatus.Text = message);
            await _modelAssets.EnsureMakeupAssetsAsync(progress);
            _makeup.Initialize();
            MakeupModelStatus.Text = "Face tracking ready • processing stays local";
            MakeupModelStatus.Foreground = (Brush)FindResource("CyanBrush");
        }
        catch (Exception ex)
        {
            AppLog.Write($"Could not prepare live makeup: {ex}");
            MakeupModelStatus.Text = $"Face tracking could not start: {ex.Message}";
            MakeupModelStatus.Foreground = (Brush)FindResource("PinkBrush");
        }
        finally
        {
            LiveMakeupCheck.IsEnabled = true;
            _makeupLoading = false;
        }
    }

    private void UpdateMakeupSettings()
    {
        if (!_uiReady) return;

        var lip = LipColorCombo.SelectedItem as ColorChoice ?? LipColors[0];
        var blush = BlushColorCombo.SelectedItem as ColorChoice ?? BlushColors[0];
        var eye = EyeColorCombo.SelectedItem as ColorChoice ?? EyeColors[0];

        _makeupSettings = new MakeupSettings
        {
            Enabled = LiveMakeupCheck.IsChecked == true,
            Lipstick = LipstickCheck.IsChecked == true,
            Blush = BlushCheck.IsChecked == true,
            Eyeshadow = EyeshadowCheck.IsChecked == true,
            Intensity = (int)Math.Round(MakeupIntensitySlider.Value),
            LipColor = lip.Hex,
            BlushColor = blush.Hex,
            EyeColor = eye.Hex
        };

        MakeupIntensityText.Text = $"{_makeupSettings.Intensity}%";
        UpdateLooksSummary();
    }

    private void ApplyMakeupSettingsToUi(MakeupSettings settings)
    {
        _uiReady = false;
        try
        {
            LiveMakeupCheck.IsChecked = settings.Enabled;
            LipstickCheck.IsChecked = settings.Lipstick;
            BlushCheck.IsChecked = settings.Blush;
            EyeshadowCheck.IsChecked = settings.Eyeshadow;
            MakeupIntensitySlider.Value = settings.Intensity;
            LipColorCombo.SelectedItem = LipColors.FirstOrDefault(x => x.Hex == settings.LipColor) ?? LipColors[0];
            BlushColorCombo.SelectedItem = BlushColors.FirstOrDefault(x => x.Hex == settings.BlushColor) ?? BlushColors[0];
            EyeColorCombo.SelectedItem = EyeColors.FirstOrDefault(x => x.Hex == settings.EyeColor) ?? EyeColors[0];
        }
        finally
        {
            _uiReady = true;
        }

        UpdateMakeupSettings();
    }

    private void RefreshLooks()
    {
        LooksList.ItemsSource = null;
        LooksList.ItemsSource = _looks.OrderByDescending(x => x.CreatedAt).ToList();
        UpdateLooksSummary();
    }

    private void UpdateLooksSummary()
    {
        if (LooksSummary is null) return;
        var s = _makeupSettings;
        LooksSummary.Text = $"Current makeup: {(s.Lipstick ? "lipstick" : "no lipstick")}, {(s.Blush ? "blush" : "no blush")}, {(s.Eyeshadow ? "eyeshadow" : "no eyeshadow")} • {s.Intensity}% intensity.\n\nSaved looks: {_looks.Count}.";
    }

    private void RefreshWardrobe()
    {
        WardrobeList.ItemsSource = null;
        WardrobeList.ItemsSource = _wardrobe;
        WardrobeEmptyText.Visibility = _wardrobe.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void StartCamera_Click(object sender, RoutedEventArgs e) => await StartSelectedCameraAsync();
    private async void RefreshCameras_Click(object sender, RoutedEventArgs e) => await RefreshCamerasAsync();

    private async void CameraCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || CameraCombo.SelectedItem is null || !_camera.IsRunning) return;
        await StartSelectedCameraAsync();
    }

    private void MirrorCheck_Changed(object sender, RoutedEventArgs e) => _camera.Mirror = MirrorCheck.IsChecked == true;

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _camera.SaveSnapshot();
            SetCameraStatus($"Saved {Path.GetFileName(path)}");
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

    private async void MakeupStartCamera_Click(object sender, RoutedEventArgs e) => await StartSelectedCameraAsync();

    private void CaptureMakeup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _camera.SaveSnapshot();
            GlobalStatus.Text = $"Saved {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            GlobalStatus.Text = ex.Message;
        }
    }

    private void MakeupControl_Changed(object sender, RoutedEventArgs e) => UpdateMakeupSettings();

    private void SaveLook_Click(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(LookNameBox.Text) ? $"Look {_looks.Count + 1}" : LookNameBox.Text.Trim();
        _looks.Add(new LookPreset { Name = name, Makeup = _makeupSettings, CreatedAt = DateTime.Now });
        _lookService.Save(_looks);
        RefreshLooks();
        GlobalStatus.Text = $"Saved “{name}”";
    }

    private void LooksList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LooksList.SelectedItem is LookPreset look)
            LooksSummary.Text = $"“{look.Name}” • {look.Makeup.Intensity}% makeup intensity • saved {look.CreatedAt:g}";
        else
            UpdateLooksSummary();
    }

    private async void LoadLook_Click(object sender, RoutedEventArgs e)
    {
        if (LooksList.SelectedItem is not LookPreset look)
        {
            GlobalStatus.Text = "Choose a saved look first";
            return;
        }

        ApplyMakeupSettingsToUi(look.Makeup);
        ShowPage("Makeup");
        await EnsureMakeupReadyAsync();
        GlobalStatus.Text = $"Loaded “{look.Name}”";
    }

    private void DeleteLook_Click(object sender, RoutedEventArgs e)
    {
        if (LooksList.SelectedItem is not LookPreset look) return;
        _looks.RemoveAll(x => x.Id == look.Id);
        _lookService.Save(_looks);
        RefreshLooks();
        GlobalStatus.Text = $"Deleted “{look.Name}”";
    }

    private void AddWardrobe_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add clothing photos to Kassie Wardrobe",
            Filter = "Image files|*.jpg;*.jpeg;*.png;*.webp;*.bmp",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true) return;

        var imported = 0;
        foreach (var file in dialog.FileNames)
        {
            try
            {
                _wardrobe.Add(_wardrobeService.Import(file));
                imported++;
            }
            catch (Exception ex)
            {
                AppLog.Write($"Wardrobe import failed for {file}: {ex}");
            }
        }

        _wardrobe = _wardrobe.OrderByDescending(x => x.AddedAt).ToList();
        _wardrobeService.Save(_wardrobe);
        RefreshWardrobe();
        GlobalStatus.Text = imported == 1 ? "1 wardrobe item added" : $"{imported} wardrobe items added";
    }

    private void RemoveWardrobe_Click(object sender, RoutedEventArgs e)
    {
        if (WardrobeList.SelectedItem is not WardrobeItem item)
        {
            GlobalStatus.Text = "Choose a wardrobe item first";
            return;
        }

        _wardrobeService.Delete(item);
        _wardrobe.RemoveAll(x => x.Id == item.Id);
        _wardrobeService.Save(_wardrobe);
        RefreshWardrobe();
        GlobalStatus.Text = $"Removed “{item.Name}”";
    }

    private void OpenWardrobe_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.Ensure();
        Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.Wardrobe) { UseShellExecute = true });
    }

    private async void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string page) return;
        ShowPage(page);

        if (page == "Makeup")
            await EnsureMakeupReadyAsync();
    }

    private void ShowPage(string page)
    {
        _activePage = page;
        PageTitle.Text = page;

        LivePage.Visibility = page == "Live Mirror" ? Visibility.Visible : Visibility.Collapsed;
        MakeupPage.Visibility = page == "Makeup" ? Visibility.Visible : Visibility.Collapsed;
        LooksPage.Visibility = page == "Looks" ? Visibility.Visible : Visibility.Collapsed;
        WardrobePage.Visibility = page == "Wardrobe" ? Visibility.Visible : Visibility.Collapsed;
        PlaceholderPage.Visibility = page is "Hair" or "Outfit" ? Visibility.Visible : Visibility.Collapsed;

        switch (page)
        {
            case "Live Mirror":
                PageSubtitle.Text = "Your starting point for every look.";
                break;
            case "Makeup":
                PageSubtitle.Text = "Live lipstick, blush and eyeshadow preview.";
                break;
            case "Looks":
                PageSubtitle.Text = "Save and reload complete looks.";
                RefreshLooks();
                break;
            case "Wardrobe":
                PageSubtitle.Text = "Your clothing library for outfit try-on.";
                RefreshWardrobe();
                break;
            case "Hair":
                PageSubtitle.Text = "Colour, cut and hairstyle previews.";
                PlaceholderIcon.Text = "⌁";
                PlaceholderTitle.Text = "Hair";
                PlaceholderText.Text = "Hair colour preview and AI hairstyle renders are the next visual feature layer after this makeup build.";
                break;
            case "Outfit":
                PageSubtitle.Text = "Live and photo outfit try-on.";
                PlaceholderIcon.Text = "♢";
                PlaceholderTitle.Text = "Outfit";
                PlaceholderText.Text = "Your Wardrobe is live now. The next stage connects those saved garments to both webcam and high-quality still-image try-on.";
                break;
        }
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
        try { _makeup.Dispose(); } catch { }
    }
}
