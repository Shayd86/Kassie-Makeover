using System.IO;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using Kassie.Makeover.Infrastructure;
using Kassie.Makeover.Models;

namespace Kassie.Makeover.Services;

public sealed class HairSegmentationService : IDisposable
{
    private readonly LocalAiAssetService _assets;
    private InferenceSession? _session;
    private bool _disposed;

    public HairSegmentationService(LocalAiAssetService assets)
    {
        _assets = assets;
    }

    public string CreateEditMask(
        string sourcePath,
        HairSettings settings,
        string outputPath)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(HairSegmentationService));

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Source portrait not found.", sourcePath);

        if (!File.Exists(_assets.SegmenterPath))
            throw new InvalidOperationException("The local hair segmentation model is not installed.");

        _session ??= new InferenceSession(_assets.SegmenterPath);

        using var source = Cv2.ImRead(sourcePath, ImreadModes.Color);
        if (source.Empty())
            throw new InvalidOperationException("The source portrait could not be opened.");

        using var small = new Mat();
        Cv2.Resize(source, small, new Size(256, 256), 0, 0, InterpolationFlags.Area);
        Cv2.CvtColor(small, small, ColorConversionCodes.BGR2RGB);

        var input = new DenseTensor<float>(new[] { 1, 256, 256, 3 });

        for (var y = 0; y < 256; y++)
        {
            for (var x = 0; x < 256; x++)
            {
                var pixel = small.At<Vec3b>(y, x);
                input[0, y, x, 0] = pixel.Item0 / 255f;
                input[0, y, x, 1] = pixel.Item1 / 255f;
                input[0, y, x, 2] = pixel.Item2 / 255f;
            }
        }

        var inputName = _session.InputMetadata.Keys.FirstOrDefault()
            ?? throw new InvalidOperationException("Hair segmenter has no input.");

        using var results = _session.Run(
            new[] { NamedOnnxValue.CreateFromTensor(inputName, input) });

        var logits = results.First().AsTensor<float>();

        if (logits.Dimensions.Count != 4 ||
            logits.Dimensions[0] != 1 ||
            logits.Dimensions[1] != 256 ||
            logits.Dimensions[2] != 256 ||
            logits.Dimensions[3] < 6)
        {
            throw new InvalidOperationException(
                $"Unexpected hair-segmentation output shape: [{string.Join(", ", logits.Dimensions)}].");
        }
        using var hair = new Mat(256, 256, MatType.CV_8UC1, Scalar.Black);
        using var face = new Mat(256, 256, MatType.CV_8UC1, Scalar.Black);

        var minX = 256;
        var minY = 256;
        var maxX = -1;
        var maxY = -1;

        // Allocate this once. Repeated stackalloc inside 65,536 loop iterations can
        // exhaust the process stack and terminate the app before normal exception handling runs.
        Span<float> values = stackalloc float[6];

        for (var y = 0; y < 256; y++)
        {
            for (var x = 0; x < 256; x++)
            {
                var maxLogit = float.NegativeInfinity;

                for (var cls = 0; cls < 6; cls++)
                {
                    var value = logits[0, y, x, cls];
                    values[cls] = value;
                    maxLogit = Math.Max(maxLogit, value);
                }

                var sum = 0f;
                for (var cls = 0; cls < 6; cls++)
                {
                    values[cls] = MathF.Exp(values[cls] - maxLogit);
                    sum += values[cls];
                }

                var hairP = values[1] / Math.Max(sum, 1e-6f);
                var faceP = values[3] / Math.Max(sum, 1e-6f);

                if (hairP >= 0.22f)
                {
                    hair.Set(y, x, (byte)Math.Clamp((int)(hairP * 255f), 80, 255));
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }

                if (faceP >= 0.18f)
                {
                    face.Set(y, x, (byte)255);
                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }
        }

        if (maxX < minX || maxY < minY)
            throw new InvalidOperationException("Kassie could not find a usable head/hair region in this photo.");

        using var edit = hair.Clone();

        var boxWidth = Math.Max(30, maxX - minX + 1);
        var boxHeight = Math.Max(40, maxY - minY + 1);
        var cx = (minX + maxX) / 2;
        var top = Math.Max(0, minY - (int)(boxHeight * 0.28));

        var lengthMultiplier = settings.Length switch
        {
            "Very short" => 0.72,
            "Short" => 0.92,
            "Long" => 1.55,
            "Very long" => 1.90,
            _ => 1.25
        };

        var outerWidth = (int)Math.Min(250, boxWidth * 1.55);
        var outerHeight = (int)Math.Min(250, boxHeight * lengthMultiplier);

        var centerY = Math.Min(
            255,
            top + Math.Max(outerHeight / 2, (int)(boxHeight * 0.48)));

        Cv2.Ellipse(
            edit,
            new Point(cx, centerY),
            new Size(Math.Max(20, outerWidth / 2), Math.Max(24, outerHeight / 2)),
            0,
            0,
            360,
            new Scalar(220),
            -1,
            LineTypes.AntiAlias);

        if (settings.Length is "Long" or "Very long")
        {
            var sideWidth = Math.Max(10, boxWidth / 3);
            var startY = Math.Clamp(minY + boxHeight / 3, 0, 255);
            var endY = Math.Clamp(startY + (int)(boxHeight * (settings.Length == "Very long" ? 1.30 : 0.95)), 0, 255);

            Cv2.Rectangle(
                edit,
                new Rect(
                    Math.Clamp(cx - boxWidth / 2 - sideWidth / 2, 0, 255),
                    startY,
                    Math.Min(sideWidth, 256 - Math.Clamp(cx - boxWidth / 2 - sideWidth / 2, 0, 255)),
                    Math.Max(1, endY - startY)),
                new Scalar(190),
                -1);

            Cv2.Rectangle(
                edit,
                new Rect(
                    Math.Clamp(cx + boxWidth / 2 - sideWidth / 2, 0, 255),
                    startY,
                    Math.Min(sideWidth, 256 - Math.Clamp(cx + boxWidth / 2 - sideWidth / 2, 0, 255)),
                    Math.Max(1, endY - startY)),
                new Scalar(190),
                -1);
        }

        // Protect the recognised face. Hair can overlap the edge naturally, but facial skin stays stable.
        using var protectedFace = new Mat();
        Cv2.Dilate(face, protectedFace, Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(7, 7)));
        edit.SetTo(Scalar.Black, protectedFace);

        // Reintroduce the original detected hair with a strong mask.
        Cv2.Max(edit, hair, edit);

        Cv2.Dilate(edit, edit, Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(9, 9)));
        Cv2.GaussianBlur(edit, edit, new Size(0, 0), 3.2);

        using var full = new Mat();
        Cv2.Resize(edit, full, source.Size(), 0, 0, InterpolationFlags.Linear);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        if (!Cv2.ImWrite(outputPath, full))
            throw new IOException("The hairstyle edit mask could not be saved.");

        AppLog.Write($"Hair segmentation mask saved: {outputPath}");
        return outputPath;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session?.Dispose();
        _session = null;
    }
}
