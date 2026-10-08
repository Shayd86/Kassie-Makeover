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
        var directory = Path.GetDirectoryName(outputPath)
                        ?? throw new InvalidOperationException("Mask output folder is invalid.");
        var result = CreateMasks(sourcePath, settings, directory, Path.GetFileNameWithoutExtension(outputPath));
        if (!string.Equals(result.EditMaskPath, outputPath, StringComparison.OrdinalIgnoreCase))
            File.Copy(result.EditMaskPath, outputPath, true);
        return outputPath;
    }

    public HairSegmentationResult CreateMasks(
        string sourcePath,
        HairSettings settings,
        string outputDirectory,
        string? prefix = null)
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
        var outputShape = logits.Dimensions.ToArray();

        if (outputShape.Length != 4 ||
            outputShape[0] != 1 ||
            outputShape[1] != 256 ||
            outputShape[2] != 256 ||
            outputShape[3] < 6)
        {
            throw new InvalidOperationException(
                $"Unexpected hair-segmentation output shape: [{string.Join(", ", outputShape)}].");
        }

        using var hair = new Mat(256, 256, MatType.CV_8UC1, Scalar.Black);
        using var face = new Mat(256, 256, MatType.CV_8UC1, Scalar.Black);

        var minX = 256;
        var minY = 256;
        var maxX = -1;
        var maxY = -1;

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
        var top = Math.Max(0, minY - (int)(boxHeight * 0.22));

        var lengthMultiplier = settings.Length switch
        {
            "Very short" => 0.76,
            "Short" => 0.94,
            "Long" => 1.48,
            "Very long" => 1.78,
            _ => 1.20
        };

        var widthMultiplier = settings.Volume switch
        {
            "Low" => 1.28,
            "High" => 1.62,
            _ => 1.46
        };

        var outerWidth = (int)Math.Min(250, boxWidth * widthMultiplier);
        var outerHeight = (int)Math.Min(250, boxHeight * lengthMultiplier);
        var centerY = Math.Min(255, top + Math.Max(outerHeight / 2, (int)(boxHeight * 0.46)));

        Cv2.Ellipse(
            edit,
            new Point(cx, centerY),
            new Size(Math.Max(20, outerWidth / 2), Math.Max(24, outerHeight / 2)),
            0,
            0,
            360,
            new Scalar(settings.Length is "Very short" or "Short" ? 175 : 205),
            -1,
            LineTypes.AntiAlias);

        if (settings.Length is "Long" or "Very long")
        {
            var sideWidth = Math.Max(10, boxWidth / 3);
            var startY = Math.Clamp(minY + boxHeight / 3, 0, 255);
            var extension = settings.Length == "Very long" ? 1.22 : 0.90;
            var endY = Math.Clamp(startY + (int)(boxHeight * extension), 0, 255);

            AddSideExpansion(edit, cx - boxWidth / 2 - sideWidth / 2, startY, sideWidth, endY - startY);
            AddSideExpansion(edit, cx + boxWidth / 2 - sideWidth / 2, startY, sideWidth, endY - startY);
        }

        using var protectedFace = new Mat();
        Cv2.Dilate(
            face,
            protectedFace,
            Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(9, 9)));

        // Preserve facial skin. Re-add only the actual detected hair afterwards so
        // natural fringe/hairline pixels can still be edited.
        edit.SetTo(Scalar.Black, protectedFace);
        Cv2.Max(edit, hair, edit);

        var dilation = settings.StrengthHint switch
        {
            "Conservative" => 5,
            "Strong" => 11,
            _ => 7
        };

        Cv2.Dilate(
            edit,
            edit,
            Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(dilation, dilation)));
        Cv2.GaussianBlur(edit, edit, new Size(0, 0), settings.StrengthHint == "Strong" ? 4.0 : 3.0);

        using var hairFull = new Mat();
        using var faceFull = new Mat();
        using var editFull = new Mat();
        Cv2.Resize(hair, hairFull, source.Size(), 0, 0, InterpolationFlags.Linear);
        Cv2.Resize(protectedFace, faceFull, source.Size(), 0, 0, InterpolationFlags.Nearest);
        Cv2.Resize(edit, editFull, source.Size(), 0, 0, InterpolationFlags.Linear);

        Directory.CreateDirectory(outputDirectory);
        prefix = string.IsNullOrWhiteSpace(prefix) ? Guid.NewGuid().ToString("N") : prefix;

        var hairPath = Path.Combine(outputDirectory, $"{prefix}-hair-mask.png");
        var facePath = Path.Combine(outputDirectory, $"{prefix}-face-mask.png");
        var editPath = Path.Combine(outputDirectory, $"{prefix}-edit-mask.png");

        if (!Cv2.ImWrite(hairPath, hairFull) ||
            !Cv2.ImWrite(facePath, faceFull) ||
            !Cv2.ImWrite(editPath, editFull))
        {
            throw new IOException("One or more hairstyle diagnostic masks could not be saved.");
        }

        AppLog.HairAi($"Hair segmentation masks saved: {editPath}");

        return new HairSegmentationResult
        {
            HairMaskPath = hairPath,
            FaceMaskPath = facePath,
            EditMaskPath = editPath
        };
    }

    private static void AddSideExpansion(Mat edit, int x, int y, int width, int height)
    {
        x = Math.Clamp(x, 0, 255);
        y = Math.Clamp(y, 0, 255);
        width = Math.Min(Math.Max(1, width), 256 - x);
        height = Math.Min(Math.Max(1, height), 256 - y);
        Cv2.Rectangle(edit, new Rect(x, y, width, height), new Scalar(175), -1);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session?.Dispose();
        _session = null;
    }
}
