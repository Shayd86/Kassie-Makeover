using System.Globalization;
using System.IO;
using OpenCvSharp;
using Kassie.Makeover.Infrastructure;
using Kassie.Makeover.Models;

namespace Kassie.Makeover.Services;

public sealed class MakeupService : IDisposable
{
    private CascadeClassifier? _face;
    private CascadeClassifier? _eyes;
    private CascadeClassifier? _smile;
    private Rect? _lastFace;
    private Rect[] _lastEyes = [];
    private Rect? _lastMouth;
    private int _frameCounter;
    private bool _disposed;

    public bool Ready => _face is not null && _eyes is not null && _smile is not null;

    public void Initialize()
    {
        DisposeClassifiers();

        var facePath = Path.Combine(AppPaths.Models, "haarcascade_frontalface_default.xml");
        var eyePath = Path.Combine(AppPaths.Models, "haarcascade_eye_tree_eyeglasses.xml");
        var smilePath = Path.Combine(AppPaths.Models, "haarcascade_smile.xml");

        _face = new CascadeClassifier(facePath);
        _eyes = new CascadeClassifier(eyePath);
        _smile = new CascadeClassifier(smilePath);

        if (!Ready)
            throw new InvalidOperationException("The face tracking files could not be loaded.");

        AppLog.Write("Live makeup face tracking initialized.");
    }

    public void Apply(Mat frame, MakeupSettings settings)
    {
        if (_disposed || !Ready || !settings.Enabled || frame.Empty())
            return;

        _frameCounter++;
        if (_frameCounter % 5 == 1 || _lastFace is null)
            Detect(frame);

        if (_lastFace is not Rect face || face.Width < 30 || face.Height < 30)
            return;

        var intensity = Math.Clamp(settings.Intensity, 0, 100) / 100.0;

        if (settings.Blush)
            DrawBlush(frame, face, ParseHex(settings.BlushColor), 0.12 + intensity * 0.28);

        if (settings.Eyeshadow && _lastEyes.Length > 0)
            DrawEyeshadow(frame, _lastEyes, ParseHex(settings.EyeColor), 0.10 + intensity * 0.32);

        if (settings.Lipstick)
            DrawLipstick(frame, face, _lastMouth, ParseHex(settings.LipColor), 0.14 + intensity * 0.42);
    }

    private void Detect(Mat frame)
    {
        try
        {
            using var gray = new Mat();
            Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.EqualizeHist(gray, gray);

            var faces = _face!.DetectMultiScale(gray, 1.12, 5, HaarDetectionTypes.ScaleImage, new Size(90, 90));
            var bestFace = faces.OrderByDescending(r => r.Width * r.Height).FirstOrDefault();

            if (bestFace.Width <= 0 || bestFace.Height <= 0)
            {
                _lastFace = null;
                _lastEyes = [];
                _lastMouth = null;
                return;
            }

            _lastFace = Smooth(_lastFace, bestFace);

            var face = _lastFace.Value;
            var upper = new Rect(face.X, face.Y, face.Width, Math.Max(1, (int)(face.Height * 0.62)));
            upper = Clamp(upper, gray.Width, gray.Height);

            using (var eyeRoi = new Mat(gray, upper))
            {
                var detectedEyes = _eyes!.DetectMultiScale(eyeRoi, 1.1, 4, HaarDetectionTypes.ScaleImage,
                    new Size(Math.Max(16, face.Width / 10), Math.Max(10, face.Height / 14)));

                _lastEyes = detectedEyes
                    .Select(r => new Rect(r.X + upper.X, r.Y + upper.Y, r.Width, r.Height))
                    .Where(r => r.Y < face.Y + face.Height * 0.58)
                    .OrderByDescending(r => r.Width * r.Height)
                    .Take(2)
                    .OrderBy(r => r.X)
                    .ToArray();
            }

            var lower = new Rect(face.X, face.Y + face.Height / 2, face.Width, face.Height / 2);
            lower = Clamp(lower, gray.Width, gray.Height);
            using var mouthRoi = new Mat(gray, lower);
            var mouths = _smile!.DetectMultiScale(mouthRoi, 1.2, 18, HaarDetectionTypes.ScaleImage,
                new Size(Math.Max(20, face.Width / 5), Math.Max(10, face.Height / 16)));

            var mouth = mouths
                .Select(r => new Rect(r.X + lower.X, r.Y + lower.Y, r.Width, r.Height))
                .OrderByDescending(r => r.Width * r.Height)
                .FirstOrDefault();

            _lastMouth = mouth.Width > 0 ? Smooth(_lastMouth, mouth) : null;
        }
        catch (Exception ex)
        {
            AppLog.Camera($"Makeup tracking error: {ex.Message}");
        }
    }

    private static Rect Smooth(Rect? previous, Rect current)
    {
        if (previous is not Rect p)
            return current;

        const double keep = 0.65;
        return new Rect(
            (int)(p.X * keep + current.X * (1 - keep)),
            (int)(p.Y * keep + current.Y * (1 - keep)),
            Math.Max(1, (int)(p.Width * keep + current.Width * (1 - keep))),
            Math.Max(1, (int)(p.Height * keep + current.Height * (1 - keep))));
    }

    private static Rect Clamp(Rect r, int width, int height)
    {
        var x = Math.Clamp(r.X, 0, Math.Max(0, width - 1));
        var y = Math.Clamp(r.Y, 0, Math.Max(0, height - 1));
        var w = Math.Clamp(r.Width, 1, Math.Max(1, width - x));
        var h = Math.Clamp(r.Height, 1, Math.Max(1, height - y));
        return new Rect(x, y, w, h);
    }

    private static void DrawBlush(Mat frame, Rect face, Scalar color, double alpha)
    {
        using var overlay = frame.Clone();
        var y = face.Y + (int)(face.Height * 0.62);
        var left = new Point(face.X + (int)(face.Width * 0.28), y);
        var right = new Point(face.X + (int)(face.Width * 0.72), y);
        var axes = new Size(Math.Max(6, (int)(face.Width * 0.13)), Math.Max(4, (int)(face.Height * 0.075)));

        Cv2.Ellipse(overlay, left, axes, -8, 0, 360, color, -1, LineTypes.AntiAlias);
        Cv2.Ellipse(overlay, right, axes, 8, 0, 360, color, -1, LineTypes.AntiAlias);
        Cv2.GaussianBlur(overlay, overlay, new Size(0, 0), Math.Max(2.0, face.Width * 0.025));
        Cv2.AddWeighted(overlay, alpha, frame, 1.0 - alpha, 0, frame);
    }

    private static void DrawEyeshadow(Mat frame, IReadOnlyList<Rect> eyes, Scalar color, double alpha)
    {
        using var overlay = frame.Clone();

        foreach (var eye in eyes)
        {
            var center = new Point(eye.X + eye.Width / 2, eye.Y + (int)(eye.Height * 0.36));
            var axes = new Size(Math.Max(5, (int)(eye.Width * 0.62)), Math.Max(3, (int)(eye.Height * 0.46)));
            Cv2.Ellipse(overlay, center, axes, 0, 190, 350, color, -1, LineTypes.AntiAlias);
        }

        Cv2.GaussianBlur(overlay, overlay, new Size(0, 0), 4.0);
        Cv2.AddWeighted(overlay, alpha, frame, 1.0 - alpha, 0, frame);
    }

    private static void DrawLipstick(Mat frame, Rect face, Rect? detectedMouth, Scalar color, double alpha)
    {
        var mouth = detectedMouth ?? new Rect(
            face.X + (int)(face.Width * 0.29),
            face.Y + (int)(face.Height * 0.72),
            (int)(face.Width * 0.42),
            Math.Max(8, (int)(face.Height * 0.12)));

        using var overlay = frame.Clone();
        var cx = mouth.X + mouth.Width / 2;
        var cy = mouth.Y + mouth.Height / 2;
        var topAxes = new Size(Math.Max(6, mouth.Width / 2), Math.Max(2, mouth.Height / 3));
        var bottomAxes = new Size(Math.Max(6, mouth.Width / 2), Math.Max(2, (int)(mouth.Height * 0.42)));

        Cv2.Ellipse(overlay, new Point(cx, cy - 1), topAxes, 0, 180, 360, color, -1, LineTypes.AntiAlias);
        Cv2.Ellipse(overlay, new Point(cx, cy + 1), bottomAxes, 0, 0, 180, color, -1, LineTypes.AntiAlias);
        Cv2.GaussianBlur(overlay, overlay, new Size(0, 0), 1.2);
        Cv2.AddWeighted(overlay, alpha, frame, 1.0 - alpha, 0, frame);
    }

    private static Scalar ParseHex(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6) return new Scalar(128, 80, 180);

        var r = byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Scalar(b, g, r);
    }

    private void DisposeClassifiers()
    {
        _face?.Dispose();
        _eyes?.Dispose();
        _smile?.Dispose();
        _face = null;
        _eyes = null;
        _smile = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeClassifiers();
    }
}
