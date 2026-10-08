using System.Globalization;
using System.IO;
using OpenCvSharp;
using Kassie.Makeover.Infrastructure;
using Kassie.Makeover.Models;

namespace Kassie.Makeover.Services;

public sealed class MakeupService : IDisposable
{
    private readonly object _stateGate = new();

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
        if (_frameCounter % 4 == 1 || _lastFace is null)
            Detect(frame);

        Rect face;
        Rect[] eyes;
        Rect? mouth;

        lock (_stateGate)
        {
            if (_lastFace is not Rect detected || detected.Width < 30 || detected.Height < 30)
                return;

            face = detected;
            eyes = _lastEyes.ToArray();
            mouth = _lastMouth;
        }

        var intensity = Math.Clamp(settings.Intensity, 0, 100) / 100.0;

        if (settings.Blush)
            DrawBlush(frame, face, ParseHex(settings.BlushColor), intensity, settings.BlushPlacement);

        if (settings.Eyeshadow && eyes.Length > 0)
            DrawEyeshadow(frame, eyes, ParseHex(settings.EyeColor), intensity, settings.EyeStyle);

        if (settings.Lipstick)
            DrawLipstick(frame, face, mouth, ParseHex(settings.LipColor), intensity, settings.LipFinish);
    }

    public FaceAnalysisSnapshot? GetAnalysisSnapshot()
    {
        lock (_stateGate)
        {
            if (_lastFace is not Rect face || face.Width <= 0 || face.Height <= 0)
                return null;

            var aspect = face.Height / (double)Math.Max(1, face.Width);

            var faceShape = aspect switch
            {
                > 1.34 => "Long-ish",
                < 1.14 => "Round-ish",
                _ => "Oval-ish"
            };

            var eyeSpacingRatio = 0.35;
            var eyeSpacing = "Balanced";

            if (_lastEyes.Length >= 2)
            {
                var ordered = _lastEyes.OrderBy(r => r.X).Take(2).ToArray();
                var leftCenter = ordered[0].X + ordered[0].Width / 2.0;
                var rightCenter = ordered[1].X + ordered[1].Width / 2.0;
                eyeSpacingRatio = (rightCenter - leftCenter) / Math.Max(1.0, face.Width);

                eyeSpacing = eyeSpacingRatio switch
                {
                    < 0.31 => "Close-set",
                    > 0.40 => "Wide-set",
                    _ => "Balanced"
                };
            }

            var mouthWidthRatio = _lastMouth is Rect mouth && mouth.Width > 0
                ? mouth.Width / (double)Math.Max(1, face.Width)
                : 0.34;

            var featureBalance = mouthWidthRatio switch
            {
                < 0.29 => "Delicate mouth emphasis",
                > 0.43 => "Strong mouth emphasis",
                _ => "Balanced mouth emphasis"
            };

            return new FaceAnalysisSnapshot(
                faceShape,
                eyeSpacing,
                featureBalance,
                aspect,
                eyeSpacingRatio,
                mouthWidthRatio,
                _lastEyes.Length >= 2 && _lastMouth is not null
                    ? "Good camera read"
                    : "Approximate camera read");
        }
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
                lock (_stateGate)
                {
                    _lastFace = null;
                    _lastEyes = [];
                    _lastMouth = null;
                }
                return;
            }

            Rect smoothedFace;
            lock (_stateGate)
            {
                _lastFace = Smooth(_lastFace, bestFace);
                smoothedFace = _lastFace.Value;
            }

            var upper = Clamp(new Rect(
                smoothedFace.X,
                smoothedFace.Y,
                smoothedFace.Width,
                Math.Max(1, (int)(smoothedFace.Height * 0.60))),
                gray.Width, gray.Height);

            Rect[] eyes;
            using (var eyeRoi = new Mat(gray, upper))
            {
                eyes = _eyes!.DetectMultiScale(
                        eyeRoi,
                        1.1,
                        4,
                        HaarDetectionTypes.ScaleImage,
                        new Size(Math.Max(16, smoothedFace.Width / 10), Math.Max(10, smoothedFace.Height / 14)))
                    .Select(r => new Rect(r.X + upper.X, r.Y + upper.Y, r.Width, r.Height))
                    .Where(r => r.Y < smoothedFace.Y + smoothedFace.Height * 0.58)
                    .OrderByDescending(r => r.Width * r.Height)
                    .Take(2)
                    .OrderBy(r => r.X)
                    .ToArray();
            }

            var lower = Clamp(new Rect(
                smoothedFace.X + (int)(smoothedFace.Width * 0.10),
                smoothedFace.Y + (int)(smoothedFace.Height * 0.52),
                (int)(smoothedFace.Width * 0.80),
                (int)(smoothedFace.Height * 0.42)),
                gray.Width, gray.Height);

            Rect? mouth = null;
            using (var mouthRoi = new Mat(gray, lower))
            {
                var detected = _smile!.DetectMultiScale(
                        mouthRoi,
                        1.18,
                        18,
                        HaarDetectionTypes.ScaleImage,
                        new Size(Math.Max(20, smoothedFace.Width / 5), Math.Max(10, smoothedFace.Height / 16)))
                    .Select(r => new Rect(r.X + lower.X, r.Y + lower.Y, r.Width, r.Height))
                    .OrderByDescending(r => r.Width * r.Height)
                    .FirstOrDefault();

                if (detected.Width > 0)
                {
                    var shrunk = new Rect(
                        detected.X + (int)(detected.Width * 0.07),
                        detected.Y + (int)(detected.Height * 0.20),
                        Math.Max(8, (int)(detected.Width * 0.86)),
                        Math.Max(6, (int)(detected.Height * 0.58)));
                    mouth = Clamp(shrunk, gray.Width, gray.Height);
                }
            }

            lock (_stateGate)
            {
                _lastEyes = eyes;
                _lastMouth = mouth is Rect m ? Smooth(_lastMouth, m) : null;
            }
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

        const double keep = 0.72;
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

    private static void DrawBlush(Mat frame, Rect face, Scalar color, double intensity, string placement)
    {
        var baseAlpha = 0.06 + intensity * 0.16;
        var lifted = placement.Equals("Lifted", StringComparison.OrdinalIgnoreCase);
        var apples = placement.Equals("Apples", StringComparison.OrdinalIgnoreCase);

        var y = face.Y + (int)(face.Height * (apples ? 0.62 : 0.58));
        var xOffset = lifted ? 0.25 : 0.29;
        var left = new Point(face.X + (int)(face.Width * xOffset), y);
        var right = new Point(face.X + (int)(face.Width * (1.0 - xOffset)), y);

        var width = Math.Max(10, (int)(face.Width * (lifted ? 0.15 : 0.13)));
        var height = Math.Max(7, (int)(face.Height * 0.055));

        ApplySoftEllipse(frame, left, new Size(width, height), lifted ? -22 : 0, color, baseAlpha, Math.Max(6, face.Width * 0.035));
        ApplySoftEllipse(frame, right, new Size(width, height), lifted ? 22 : 0, color, baseAlpha, Math.Max(6, face.Width * 0.035));

        if (placement.Equals("Sun-kissed", StringComparison.OrdinalIgnoreCase))
        {
            var nose = new Point(face.X + face.Width / 2, face.Y + (int)(face.Height * 0.59));
            ApplySoftEllipse(frame, nose, new Size(Math.Max(8, face.Width / 14), Math.Max(5, face.Height / 34)), 0, color, baseAlpha * 0.45, Math.Max(5, face.Width * 0.025));
        }
    }

    private static void DrawEyeshadow(Mat frame, IReadOnlyList<Rect> eyes, Scalar color, double intensity, string style)
    {
        var alpha = 0.07 + intensity * 0.20;

        foreach (var eye in eyes)
        {
            var outerLift = style.Equals("Outer lift", StringComparison.OrdinalIgnoreCase);
            var smoky = style.Equals("Soft smoky", StringComparison.OrdinalIgnoreCase);

            var centerX = eye.X + eye.Width / 2 + (outerLift ? (int)(eye.Width * 0.10) : 0);
            var centerY = eye.Y + (int)(eye.Height * 0.24);
            var axes = new Size(
                Math.Max(6, (int)(eye.Width * (outerLift ? 0.72 : 0.60))),
                Math.Max(3, (int)(eye.Height * (smoky ? 0.48 : 0.34))));

            ApplySoftEllipse(
                frame,
                new Point(centerX, centerY),
                axes,
                outerLift ? -8 : 0,
                color,
                alpha * (smoky ? 1.15 : 1.0),
                Math.Max(2.5, eye.Width * 0.10));
        }
    }

    private static void DrawLipstick(Mat frame, Rect face, Rect? detectedMouth, Scalar color, double intensity, string finish)
    {
        var mouth = detectedMouth ?? new Rect(
            face.X + (int)(face.Width * 0.34),
            face.Y + (int)(face.Height * 0.735),
            Math.Max(18, (int)(face.Width * 0.32)),
            Math.Max(8, (int)(face.Height * 0.075)));

        mouth = Clamp(mouth, frame.Width, frame.Height);

        var padded = Clamp(
            new Rect(mouth.X - 5, mouth.Y - 4, mouth.Width + 10, mouth.Height + 8),
            frame.Width,
            frame.Height);

        using var target = new Mat(frame, padded);
        using var mask = new Mat(target.Rows, target.Cols, MatType.CV_8UC1, Scalar.Black);

        var cx = mouth.X + mouth.Width / 2 - padded.X;
        var cy = mouth.Y + mouth.Height / 2 - padded.Y;

        var topWidth = Math.Max(6, (int)(mouth.Width * 0.46));
        var topHeight = Math.Max(2, (int)(mouth.Height * 0.34));
        var bottomWidth = Math.Max(6, (int)(mouth.Width * 0.47));
        var bottomHeight = Math.Max(2, (int)(mouth.Height * 0.34));

        Cv2.Ellipse(mask, new Point(cx, cy - 1), new Size(topWidth, topHeight), 0, 180, 360, Scalar.White, -1, LineTypes.AntiAlias);
        Cv2.Ellipse(mask, new Point(cx, cy + 1), new Size(bottomWidth, bottomHeight), 0, 0, 180, Scalar.White, -1, LineTypes.AntiAlias);

        // Keep the mouth opening and the very corners clear so the colour reads as lipstick, not a sticker.
        Cv2.Ellipse(mask, new Point(cx, cy), new Size(Math.Max(3, (int)(mouth.Width * 0.32)), Math.Max(1, (int)(mouth.Height * 0.10))), 0, 0, 360, Scalar.Black, -1, LineTypes.AntiAlias);
        Cv2.GaussianBlur(mask, mask, new Size(0, 0), 0.85);

        var alpha = finish switch
        {
            "Tint" => 0.10 + intensity * 0.18,
            "Matte" => 0.18 + intensity * 0.30,
            "Gloss" => 0.14 + intensity * 0.26,
            _ => 0.14 + intensity * 0.24
        };

        BlendColour(target, mask, color, alpha);

        if (finish.Equals("Gloss", StringComparison.OrdinalIgnoreCase))
        {
            using var glossMask = new Mat(target.Rows, target.Cols, MatType.CV_8UC1, Scalar.Black);
            Cv2.Ellipse(
                glossMask,
                new Point(cx, cy - Math.Max(1, mouth.Height / 8)),
                new Size(Math.Max(3, mouth.Width / 5), Math.Max(1, mouth.Height / 12)),
                0,
                190,
                350,
                Scalar.White,
                -1,
                LineTypes.AntiAlias);
            Cv2.GaussianBlur(glossMask, glossMask, new Size(0, 0), 1.1);
            BlendColour(target, glossMask, new Scalar(235, 235, 245), 0.10 + intensity * 0.08);
        }
    }

    private static void ApplySoftEllipse(Mat frame, Point center, Size axes, double angle, Scalar color, double alpha, double blurSigma)
    {
        var padX = axes.Width * 2 + 12;
        var padY = axes.Height * 2 + 12;
        var roi = Clamp(new Rect(center.X - padX, center.Y - padY, padX * 2, padY * 2), frame.Width, frame.Height);

        using var target = new Mat(frame, roi);
        using var mask = new Mat(target.Rows, target.Cols, MatType.CV_8UC1, Scalar.Black);
        var localCenter = new Point(center.X - roi.X, center.Y - roi.Y);

        Cv2.Ellipse(mask, localCenter, axes, angle, 0, 360, Scalar.White, -1, LineTypes.AntiAlias);
        Cv2.GaussianBlur(mask, mask, new Size(0, 0), blurSigma);
        BlendColour(target, mask, color, alpha);
    }

    private static void BlendColour(Mat target, Mat mask8, Scalar color, double strength)
    {
        strength = Math.Clamp(strength, 0.0, 0.9);
        if (strength <= 0.001 || target.Empty())
            return;

        using var tinted = new Mat(target.Size(), target.Type(), color);
        using var weightsTint = new Mat();
        using var weightsBase = new Mat();

        mask8.ConvertTo(weightsTint, MatType.CV_32FC1, strength / 255.0);
        weightsBase.Create(weightsTint.Size(), MatType.CV_32FC1);
        weightsBase.SetTo(Scalar.All(1.0));
        Cv2.Subtract(weightsBase, weightsTint, weightsBase);

        Cv2.BlendLinear(tinted, target, weightsTint, weightsBase, target);
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
