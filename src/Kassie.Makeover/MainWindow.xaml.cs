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

    private static readonly ColorChoice[] HairColors =
    [
        new("Soft Black", "#252225"),
        new("Espresso", "#3A2722"),
        new("Chocolate", "#5A382A"),
        new("Chestnut", "#754834"),
        new("Copper", "#A95832"),
        new("Auburn", "#7A342D"),
        new("Honey Blonde", "#C79A58"),
        new("Ash Blonde", "#B7AA91"),
        new("Silver", "#A8A9AF"),
        new("Pink", "#C85C91"),
        new("Purple", "#76518E"),
        new("Blue Black", "#26384B")
    ];

    private readonly CameraService _camera = new();
    private readonly ModelAssetService _modelAssets = new();
    private readonly MakeupService _makeup = new();
    private readonly MakeupAdvisorService _advisor = new();
    private readonly HairService _hair = new();
    private readonly HairReferenceService _hairReferenceService = new();
    private readonly HairTryOnService _hairTryOn = new();
    private readonly LocalAiAssetService _localAiAssets = new();
    private readonly HairSegmentationService _hairSegmentation;
    private readonly HairGenerationService _hairGeneration;
    private readonly LookPresetService _lookService = new();
    private readonly WardrobeService _wardrobeService = new();

    private WriteableBitmap? _previewBitmap;
    private MakeupSettings _makeupSettings = new();
    private HairSettings _hairSettings = new();
    private MakeupRecommendation? _lastRecommendation;
    private List<LookPreset> _looks = [];
    private List<WardrobeItem> _wardrobe = [];
    private List<HairReference> _hairReferences = [];
    private string? _hairSourcePath;
    private string? _lastHairResultPath;
    private CancellationTokenSource? _hairGenerationCts;
    private string _hairMode = "Hairstyles";
    private string _activePage = "Live Mirror";
    private bool _closing;
    private bool _shutdownComplete;
    private bool _uiReady;
    private bool _makeupLoading;

    public MainWindow()
    {
        _hairSegmentation = new HairSegmentationService(_localAiAssets);
        _hairGeneration = new HairGenerationService(_localAiAssets, _hairSegmentation);

        InitializeComponent();

        LipColorCombo.ItemsSource = LipColors;
        BlushColorCombo.ItemsSource = BlushColors;
        EyeColorCombo.ItemsSource = EyeColors;
        LipFinishCombo.ItemsSource = new[] { "Tint", "Satin", "Matte", "Gloss" };
        BlushPlacementCombo.ItemsSource = new[] { "Lifted", "Apples", "Sun-kissed" };
        EyeStyleCombo.ItemsSource = new[] { "Soft wash", "Soft smoky", "Outer lift" };
        StyleVibeCombo.ItemsSource = new[] { "Everyday", "Soft glam", "Evening", "Bold" };

        HairColorCombo.ItemsSource = HairColors;
        HairFinishCombo.ItemsSource = new[] { "Natural", "Glossy", "Matte" };
        HairCoverageCombo.ItemsSource = new[] { "Full", "Roots", "Highlights" };
        HairStyleFamilyCombo.ItemsSource = new[] { "Pixie", "Bob", "Lob", "Layered", "Shag", "Long waves", "Ponytail", "Bun / updo" };
        HairLengthCombo.ItemsSource = new[] { "Very short", "Short", "Medium", "Long", "Very long" };
        HairTextureCombo.ItemsSource = new[] { "Straight", "Wavy", "Curly", "Coily" };
        HairFringeCombo.ItemsSource = new[] { "None", "Soft", "Full", "Curtain", "Side" };
        HairVolumeCombo.ItemsSource = new[] { "Low", "Natural", "High" };

        LipColorCombo.SelectedIndex = 0;
        BlushColorCombo.SelectedIndex = 0;
        EyeColorCombo.SelectedIndex = 0;
        LipFinishCombo.SelectedItem = "Satin";
        BlushPlacementCombo.SelectedItem = "Lifted";
        EyeStyleCombo.SelectedItem = "Soft wash";
        StyleVibeCombo.SelectedItem = "Everyday";

        HairColorCombo.SelectedItem = HairColors.First(x => x.Name == "Chestnut");
        HairFinishCombo.SelectedItem = "Natural";
        HairStyleFamilyCombo.SelectedItem = "Layered";
        HairCoverageCombo.SelectedItem = "Full";
        HairLengthCombo.SelectedItem = "Medium";
        HairTextureCombo.SelectedItem = "Wavy";
        HairFringeCombo.SelectedItem = "None";
        HairVolumeCombo.SelectedItem = "Natural";

        _camera.FrameProcessor = ProcessFrame;
        _camera.FrameReady += Camera_FrameReady;
        _camera.StatusChanged += (_, status) => Dispatcher.BeginInvoke(() => SetCameraStatus(status));
        _camera.Error += (_, message) => Dispatcher.BeginInvoke(() => ShowCameraError(message));

        Loaded += MainWindow_Loaded;
        _uiReady = true;
        UpdateMakeupSettings();
        UpdateHairSettings();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Kassie Makeover  •  v{version?.Major}.{version?.Minor}.{version?.Build}  •  C# / WPF";

        _looks = _lookService.Load();
        RefreshLooks();

        _wardrobe = _wardrobeService.Load();
        RefreshWardrobe();

        _hairReferences = _hairReferenceService.Load();
        RefreshHairReferences();
        UpdateLocalAiStatus();

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
            HairCameraPlaceholder.Visibility = Visibility.Collapsed;
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
        if (_activePage == "Makeup")
        {
            var makeup = _makeupSettings;
            if (makeup.Enabled && _makeup.Ready)
                _makeup.Apply(frame, makeup);
            return;
        }

        if (_activePage == "Hair" && _hairMode == "Colour")
        {
            var hair = _hairSettings;
            if (hair.Enabled && _makeup.Ready)
            {
                _makeup.Track(frame);
                _hair.Apply(frame, hair, _makeup.GetTrackedFaceRect());
            }
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
                    _previewBitmap = new WriteableBitmap(e.Width, e.Height, 96, 96, PixelFormats.Bgra32, null);

                _previewBitmap.WritePixels(new Int32Rect(0, 0, e.Width, e.Height), e.Data, e.BufferSize, e.Stride);
                PreviewImage.Source = _previewBitmap;
                MakeupPreviewImage.Source = _previewBitmap;
                HairPreviewImage.Source = _previewBitmap;
                CameraPlaceholder.Visibility = Visibility.Collapsed;
                MakeupCameraPlaceholder.Visibility = Visibility.Collapsed;
                HairCameraPlaceholder.Visibility = Visibility.Collapsed;
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
        HairCameraPlaceholder.Visibility = Visibility.Visible;
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
            EyeColor = eye.Hex,
            LipFinish = LipFinishCombo.SelectedItem as string ?? "Satin",
            BlushPlacement = BlushPlacementCombo.SelectedItem as string ?? "Lifted",
            EyeStyle = EyeStyleCombo.SelectedItem as string ?? "Soft wash"
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
            LipFinishCombo.SelectedItem = settings.LipFinish;
            BlushPlacementCombo.SelectedItem = settings.BlushPlacement;
            EyeStyleCombo.SelectedItem = settings.EyeStyle;
        }
        finally
        {
            _uiReady = true;
        }

        UpdateMakeupSettings();
    }

    private void UpdateHairSettings()
    {
        if (!_uiReady) return;

        var colour = HairColorCombo.SelectedItem as ColorChoice
                     ?? HairColors.First(x => x.Name == "Chestnut");

        _hairSettings = new HairSettings
        {
            Enabled = LiveHairCheck.IsChecked == true,
            Intensity = (int)Math.Round(HairIntensitySlider.Value),
            Color = colour.Hex,
            Finish = HairFinishCombo.SelectedItem as string ?? "Natural",
            Coverage = HairCoverageCombo.SelectedItem as string ?? "Full",
            StyleFamily = HairStyleFamilyCombo.SelectedItem as string ?? "Layered",
            Length = HairLengthCombo.SelectedItem as string ?? "Medium",
            Texture = HairTextureCombo.SelectedItem as string ?? "Wavy",
            Fringe = HairFringeCombo.SelectedItem as string ?? "None",
            Volume = HairVolumeCombo.SelectedItem as string ?? "Natural",
            ReferencePath = _hairSettings.ReferencePath
        };

        HairIntensityText.Text = $"{_hairSettings.Intensity}%";
        HairStyleSummary.Text =
            $"{_hairSettings.StyleFamily} • {_hairSettings.Length} • {_hairSettings.Texture} • " +
            $"{(_hairSettings.Fringe == "None" ? "No fringe" : _hairSettings.Fringe + " fringe")} • {_hairSettings.Volume} volume";

        UpdateLooksSummary();
    }

    private void ApplyHairSettingsToUi(HairSettings settings)
    {
        _uiReady = false;
        try
        {
            LiveHairCheck.IsChecked = settings.Enabled;
            HairIntensitySlider.Value = settings.Intensity;
            HairColorCombo.SelectedItem = HairColors.FirstOrDefault(x => x.Hex == settings.Color)
                                          ?? HairColors.First(x => x.Name == "Chestnut");
            HairFinishCombo.SelectedItem = settings.Finish;
            HairCoverageCombo.SelectedItem = settings.Coverage;
            HairStyleFamilyCombo.SelectedItem = settings.StyleFamily;
            HairLengthCombo.SelectedItem = settings.Length;
            HairTextureCombo.SelectedItem = settings.Texture;
            HairFringeCombo.SelectedItem = settings.Fringe;
            HairVolumeCombo.SelectedItem = settings.Volume;
            _hairSettings = settings;
        }
        finally
        {
            _uiReady = true;
        }

        UpdateHairSettings();

        if (!string.IsNullOrWhiteSpace(settings.ReferencePath))
        {
            var matching = _hairReferences.FirstOrDefault(x =>
                string.Equals(x.FilePath, settings.ReferencePath, StringComparison.OrdinalIgnoreCase));
            if (matching is not null)
                HairReferenceList.SelectedItem = matching;
        }
    }

    private void RefreshHairReferences()
    {
        HairReferenceList.ItemsSource = null;
        HairReferenceList.ItemsSource = _hairReferences.OrderByDescending(x => x.AddedAt).ToList();

        if (string.IsNullOrWhiteSpace(_hairSettings.ReferencePath))
            SelectedHairReferenceText.Text = _hairReferences.Count == 0
                ? "No reference yet. Add a hairstyle photo you like."
                : "Choose a reference and click Use selected.";
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
        var h = _hairSettings;
        LooksSummary.Text =
            $"Current makeup: {(s.Lipstick ? "lipstick" : "no lipstick")}, {(s.Blush ? "blush" : "no blush")}, {(s.Eyeshadow ? "eyeshadow" : "no eyeshadow")} • {s.Intensity}% intensity.\n" +
            $"Current hair: {h.StyleFamily}, {h.Length}, {h.Texture} • {h.Coverage.ToLowerInvariant()} colour at {h.Intensity}%.\n\n" +
            $"Saved looks: {_looks.Count}.";
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

    private void MakeupMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string mode) return;

        var styleMode = mode == "StyleMe";
        TryOnPanel.Visibility = styleMode ? Visibility.Collapsed : Visibility.Visible;
        StyleAdvisorPanel.Visibility = styleMode ? Visibility.Visible : Visibility.Collapsed;
        MakeupModeHint.Text = styleMode
            ? "STYLE ME • Face-aware suggestions with your chosen vibe."
            : "TRY ON • Choose shades, finishes and placement.";
        MakeupModeHint.Foreground = (Brush)FindResource(styleMode ? "PinkBrush" : "CyanBrush");
    }

    private async void AnalyseFace_Click(object sender, RoutedEventArgs e)
    {
        if (!_camera.IsRunning)
        {
            await StartSelectedCameraAsync();
            if (!_camera.IsRunning)
            {
                FaceAnalysisText.Text = "I need a live camera image before I can analyse anything.";
                return;
            }
        }

        await EnsureMakeupReadyAsync();

        var analysis = _makeup.GetAnalysisSnapshot();
        if (analysis is null)
        {
            FaceAnalysisText.Text = "I cannot get a stable face read yet. Face the camera fairly straight-on with your whole face visible, then try again.";
            ApplyRecommendationButton.IsEnabled = false;
            return;
        }

        FaceAnalysisText.Text =
            $"Face: {analysis.FaceShape}\n" +
            $"Eyes: {analysis.EyeSpacing}\n" +
            $"Features: {analysis.FeatureBalance}\n" +
            $"{analysis.ConfidenceNote}";

        var vibe = StyleVibeCombo.SelectedItem as string ?? "Everyday";
        _lastRecommendation = _advisor.Recommend(analysis, vibe);

        RecommendationTitle.Text = _lastRecommendation.Title;
        RecommendationSummary.Text = _lastRecommendation.Summary;
        RecommendationWhy.Text = _lastRecommendation.Why;
        ApplyRecommendationButton.IsEnabled = true;
        GlobalStatus.Text = "Style suggestion ready";
    }

    private async void ApplyRecommendation_Click(object sender, RoutedEventArgs e)
    {
        if (_lastRecommendation is null) return;

        ApplyMakeupSettingsToUi(_lastRecommendation.Settings);
        TryOnPanel.Visibility = Visibility.Visible;
        StyleAdvisorPanel.Visibility = Visibility.Collapsed;
        MakeupModeHint.Text = "TRY ON • Kassie’s suggestion is now live.";
        MakeupModeHint.Foreground = (Brush)FindResource("CyanBrush");

        await EnsureMakeupReadyAsync();
        GlobalStatus.Text = $"Trying {_lastRecommendation.Title}";
    }

    private async void HairStartCamera_Click(object sender, RoutedEventArgs e)
    {
        await StartSelectedCameraAsync();
        await EnsureHairTrackingReadyAsync();
    }

    private async Task EnsureHairTrackingReadyAsync()
    {
        if (!_makeup.Ready)
            await EnsureMakeupReadyAsync();

        HairTrackingStatus.Text = _makeup.Ready
            ? "Face anchor ready • live hair mask is local"
            : "Hair tracking could not start";
        HairTrackingStatus.Foreground = (Brush)FindResource(_makeup.Ready ? "CyanBrush" : "PinkBrush");
    }

    private async void HairMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string mode) return;

        _hairMode = mode;
        var colour = mode == "Colour";

        HairstylePanel.Visibility = colour ? Visibility.Collapsed : Visibility.Visible;
        HairstyleWorkspace.Visibility = colour ? Visibility.Collapsed : Visibility.Visible;
        HairColourPanel.Visibility = colour ? Visibility.Visible : Visibility.Collapsed;
        HairColourWorkspace.Visibility = colour ? Visibility.Visible : Visibility.Collapsed;

        HairModeHint.Text = colour
            ? "HAIR COLOUR • Quick live beta preview."
            : "HAIRSTYLES • Capture or import a portrait, then build the style.";
        HairModeHint.Foreground = (Brush)FindResource(colour ? "PinkBrush" : "CyanBrush");

        if (colour)
            await EnsureHairTrackingReadyAsync();
    }

    private async void CaptureHairstyleSource_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_camera.IsRunning)
                await StartSelectedCameraAsync();

            if (!_camera.IsRunning)
            {
                HairSourceStatusText.Text = "Camera could not be started.";
                return;
            }

            var path = _camera.SaveSnapshotTo(AppPaths.HairInputs, "Hair-Source");
            SetHairstyleSource(path, "Camera capture");
            GlobalStatus.Text = "Hairstyle source captured";
        }
        catch (Exception ex)
        {
            AppLog.Write($"Hairstyle camera capture failed: {ex}");
            HairSourceStatusText.Text = ex.Message;
        }
    }

    private void ImportHairstyleSource_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a portrait for hairstyle try-on",
            Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var path = _hairTryOn.ImportSource(dialog.FileName);
            SetHairstyleSource(path, $"Imported {Path.GetFileName(dialog.FileName)}");
            GlobalStatus.Text = "Hairstyle source imported";
        }
        catch (Exception ex)
        {
            AppLog.Write($"Hairstyle source import failed: {ex}");
            HairSourceStatusText.Text = ex.Message;
        }
    }

    private void ClearHairstyleSource_Click(object sender, RoutedEventArgs e)
    {
        _hairSourcePath = null;
        HairstyleSourceImage.Source = null;
        HairstyleSourcePlaceholder.Visibility = Visibility.Visible;
        HairstyleResultImage.Source = null;
        HairstyleResultPlaceholder.Visibility = Visibility.Visible;
        _lastHairResultPath = null;
        SaveHairResultButton.IsEnabled = false;
        HairSourceStatusText.Text = "No source photo selected.";
        HairRenderStatusText.Text = "Choose a source photo first.";
        HairstyleResultStatusText.Text = "Choose a source photo and hairstyle, then click Generate hairstyle.";
    }

    private void SetHairstyleSource(string path, string description)
    {
        _hairSourcePath = path;
        HairstyleSourceImage.Source = LoadBitmapUnlocked(path);
        HairstyleSourcePlaceholder.Visibility = Visibility.Collapsed;
        HairstyleResultImage.Source = null;
        HairstyleResultPlaceholder.Visibility = Visibility.Visible;
        _lastHairResultPath = null;
        SaveHairResultButton.IsEnabled = false;
        HairSourceStatusText.Text = $"{description} • {Path.GetFileName(path)}";
        HairRenderStatusText.Text = "Source ready. Choose a hairstyle and click Generate hairstyle.";
        HairstyleResultStatusText.Text = "Source ready. Generate a hairstyle to see the edited portrait here.";
    }

    private void UpdateLocalAiStatus()
    {
        if (_localAiAssets.IsReady)
        {
            LocalAiStatusText.Text = "Local hairstyle AI ready • no API key • no per-image charge.";
            LocalAiStatusText.Foreground = (Brush)FindResource("CyanBrush");
            GenerateHairstyleButton.IsEnabled = true;
        }
        else
        {
            LocalAiStatusText.Text = "Not installed yet. First setup downloads about 1.7 GB to D:\\Kassie\\Makeover.";
            LocalAiStatusText.Foreground = (Brush)FindResource("PinkBrush");
            GenerateHairstyleButton.IsEnabled = true;
        }
    }

    private async void PrepareLocalAi_Click(object sender, RoutedEventArgs e)
    {
        if (_hairGenerationCts is not null)
            return;

        PrepareLocalAiButton.IsEnabled = false;
        LocalAiProgress.Visibility = Visibility.Visible;

        try
        {
            _hairGenerationCts = new CancellationTokenSource();
            var progress = new Progress<string>(message =>
            {
                LocalAiStatusText.Text = message;
                GlobalStatus.Text = message;
            });

            await _localAiAssets.EnsureReadyAsync(progress, _hairGenerationCts.Token);
            UpdateLocalAiStatus();
            GlobalStatus.Text = "Local hairstyle AI ready";
        }
        catch (OperationCanceledException)
        {
            LocalAiStatusText.Text = "Local AI setup cancelled. Any partial download is kept so it can resume.";
            GlobalStatus.Text = "Local AI setup cancelled";
        }
        catch (Exception ex)
        {
            AppLog.Write($"Local AI setup failed: {ex}");
            LocalAiStatusText.Text = ex.Message;
            LocalAiStatusText.Foreground = (Brush)FindResource("PinkBrush");
            GlobalStatus.Text = "Local AI setup failed";
        }
        finally
        {
            _hairGenerationCts?.Dispose();
            _hairGenerationCts = null;
            PrepareLocalAiButton.IsEnabled = true;
            LocalAiProgress.Visibility = Visibility.Collapsed;
        }
    }

    private async void GenerateHairstyle_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_hairSourcePath) || !File.Exists(_hairSourcePath))
        {
            HairRenderStatusText.Text = "Capture or import a source portrait first.";
            GlobalStatus.Text = "Hairstyle source required";
            return;
        }

        if (_hairGenerationCts is not null)
            return;

        try
        {
            UpdateHairSettings();
            var request = _hairTryOn.Prepare(_hairSourcePath, _hairSettings);

            _hairGenerationCts = new CancellationTokenSource();
            GenerateHairstyleButton.IsEnabled = false;
            PrepareLocalAiButton.IsEnabled = false;
            CancelHairstyleButton.Visibility = Visibility.Visible;
            HairGenerationProgress.Visibility = Visibility.Visible;
            SaveHairResultButton.IsEnabled = false;

            var progress = new Progress<string>(message =>
            {
                HairRenderStatusText.Text = message;
                LocalAiStatusText.Text = message;
                GlobalStatus.Text = message;
            });

            HairRenderStatusText.Text =
                _localAiAssets.IsReady
                    ? $"Generating {request.Settings.StyleFamily} locally…"
                    : "Setting up local AI first…";

            HairstyleResultStatusText.Text =
                "Kassie is finding the hair region and generating the new hairstyle on this PC.";

            var result = await _hairGeneration.GenerateAsync(
                request,
                progress,
                _hairGenerationCts.Token);

            _lastHairResultPath = result.OutputPath;
            HairstyleResultImage.Source = LoadBitmapUnlocked(result.OutputPath);
            HairstyleResultPlaceholder.Visibility = Visibility.Collapsed;
            SaveHairResultButton.IsEnabled = true;

            HairRenderStatusText.Text =
                $"Done • {request.Settings.StyleFamily} • {result.Model}";
            UpdateLocalAiStatus();
            GlobalStatus.Text = "Hairstyle generated locally";
        }
        catch (OperationCanceledException)
        {
            HairRenderStatusText.Text = "Hairstyle generation cancelled.";
            HairstyleResultStatusText.Text =
                "Generation was cancelled. Partial downloads are kept so setup can resume.";
            GlobalStatus.Text = "Generation cancelled";
        }
        catch (Exception ex)
        {
            AppLog.Write($"Local hairstyle generation failed: {ex}");
            HairRenderStatusText.Text = ex.Message;
            HairstyleResultStatusText.Text =
                "Generation failed. Your source photo is unchanged; check D:\\Kassie\\Makeover\\logs for details.";
            GlobalStatus.Text = "Hairstyle generation failed";
        }
        finally
        {
            _hairGenerationCts?.Dispose();
            _hairGenerationCts = null;
            GenerateHairstyleButton.IsEnabled = true;
            PrepareLocalAiButton.IsEnabled = true;
            CancelHairstyleButton.Visibility = Visibility.Collapsed;
            HairGenerationProgress.Visibility = Visibility.Collapsed;
            LocalAiProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void CancelHairstyle_Click(object sender, RoutedEventArgs e)
    {
        _hairGenerationCts?.Cancel();
    }

    private void SaveHairResult_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_lastHairResultPath) || !File.Exists(_lastHairResultPath))
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Save hairstyle result",
            Filter = "PNG image|*.png",
            FileName = $"Kassie-Hairstyle-{DateTime.Now:yyyyMMdd-HHmmss}.png",
            AddExtension = true,
            DefaultExt = ".png"
        };

        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            File.Copy(_lastHairResultPath, dialog.FileName, true);
            GlobalStatus.Text = $"Saved {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            GlobalStatus.Text = $"Could not save result: {ex.Message}";
        }
    }

    private static BitmapImage LoadBitmapUnlocked(string path)
    {
        using var stream = File.OpenRead(path);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void HairControl_Changed(object sender, RoutedEventArgs e) => UpdateHairSettings();

    private void CaptureHair_Click(object sender, RoutedEventArgs e)
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

    private void AddHairReference_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add hairstyle references",
            Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true) return;

        var imported = 0;
        foreach (var file in dialog.FileNames)
        {
            try
            {
                _hairReferences.Add(_hairReferenceService.Import(file));
                imported++;
            }
            catch (Exception ex)
            {
                AppLog.Write($"Hair reference import failed for {file}: {ex}");
            }
        }

        _hairReferences = _hairReferences.OrderByDescending(x => x.AddedAt).ToList();
        _hairReferenceService.Save(_hairReferences);
        RefreshHairReferences();
        GlobalStatus.Text = imported == 1 ? "1 hair reference added" : $"{imported} hair references added";
    }

    private void UseHairReference_Click(object sender, RoutedEventArgs e)
    {
        if (HairReferenceList.SelectedItem is not HairReference item)
        {
            GlobalStatus.Text = "Choose a hair reference first";
            return;
        }

        _hairSettings = _hairSettings with { ReferencePath = item.FilePath };
        SelectedHairReferenceText.Text = $"Using: {item.Name}";
        UpdateLooksSummary();
        GlobalStatus.Text = $"Hair reference set to “{item.Name}”";
    }

    private void RemoveHairReference_Click(object sender, RoutedEventArgs e)
    {
        if (HairReferenceList.SelectedItem is not HairReference item)
        {
            GlobalStatus.Text = "Choose a hair reference first";
            return;
        }

        _hairReferenceService.Delete(item);
        _hairReferences.RemoveAll(x => x.Id == item.Id);
        _hairReferenceService.Save(_hairReferences);

        if (string.Equals(_hairSettings.ReferencePath, item.FilePath, StringComparison.OrdinalIgnoreCase))
            _hairSettings = _hairSettings with { ReferencePath = null };

        RefreshHairReferences();
        GlobalStatus.Text = $"Removed “{item.Name}”";
    }

    private void HairReferenceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HairReferenceList.SelectedItem is HairReference item)
        {
            var inUse = string.Equals(_hairSettings.ReferencePath, item.FilePath, StringComparison.OrdinalIgnoreCase);
            SelectedHairReferenceText.Text = inUse
                ? $"Using: {item.Name}"
                : $"Selected: {item.Name} • click Use selected to attach it to this look";
        }
    }

    private void OpenHairFolder_Click(object sender, RoutedEventArgs e)
    {
        AppPaths.Ensure();
        Process.Start(new ProcessStartInfo("explorer.exe", AppPaths.Hair) { UseShellExecute = true });
    }

    private void SaveLook_Click(object sender, RoutedEventArgs e)
    {
        var name = string.IsNullOrWhiteSpace(LookNameBox.Text) ? $"Look {_looks.Count + 1}" : LookNameBox.Text.Trim();
        _looks.Add(new LookPreset
        {
            Name = name,
            Makeup = _makeupSettings,
            Hair = _hairSettings,
            CreatedAt = DateTime.Now
        });
        _lookService.Save(_looks);
        RefreshLooks();
        GlobalStatus.Text = $"Saved “{name}”";
    }

    private void LooksList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LooksList.SelectedItem is LookPreset look)
            LooksSummary.Text = $"“{look.Name}” • {look.Makeup.Intensity}% makeup • {look.Hair.StyleFamily} / {look.Hair.Length} {look.Hair.Texture.ToLowerInvariant()} hair • saved {look.CreatedAt:g}";
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
        ApplyHairSettingsToUi(look.Hair ?? new HairSettings());
        ShowPage("Makeup");
        await EnsureMakeupReadyAsync();
        GlobalStatus.Text = $"Loaded “{look.Name}” • makeup and hair restored";
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
            Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp",
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
        HairPage.Visibility = page == "Hair" ? Visibility.Visible : Visibility.Collapsed;
        LooksPage.Visibility = page == "Looks" ? Visibility.Visible : Visibility.Collapsed;
        WardrobePage.Visibility = page == "Wardrobe" ? Visibility.Visible : Visibility.Collapsed;
        PlaceholderPage.Visibility = page == "Outfit" ? Visibility.Visible : Visibility.Collapsed;

        switch (page)
        {
            case "Live Mirror":
                PageSubtitle.Text = "Your starting point for every look.";
                break;
            case "Makeup":
                PageSubtitle.Text = "Try makeup live, or get face-aware style suggestions.";
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
                PageSubtitle.Text = "Try hairstyles from a photo, with live hair colour as a secondary beta.";
                RefreshHairReferences();
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

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_shutdownComplete)
            return;

        // Keep WPF's dispatcher alive while the camera thread is shutting down.
        // The preview thread can be inside Dispatcher.Invoke; blocking the UI
        // here with synchronous Dispose() can leave that thread and the webcam
        // handle alive after the visible window has gone.
        e.Cancel = true;

        if (_closing)
            return;

        _closing = true;
        _hairGenerationCts?.Cancel();
        GlobalStatus.Text = "Closing camera…";
        AppLog.Camera("Window close requested. Waiting for camera shutdown before closing WPF.");

        try
        {
            _camera.FrameProcessor = null;
            await _camera.StopAsync();
        }
        catch (Exception ex)
        {
            AppLog.Camera($"Camera shutdown during window close failed: {ex}");
        }

        try { _camera.Dispose(); } catch (Exception ex) { AppLog.Camera($"Camera dispose fallback failed: {ex.Message}"); }
        try { _makeup.Dispose(); } catch { }
        try { _hairSegmentation.Dispose(); } catch { }

        _shutdownComplete = true;
        AppLog.Camera("Camera shutdown complete. Closing WPF window.");
        Close();
    }
}
