using System.IO;
using OpenCvSharp;
using Kassie.Makeover.Infrastructure;
using Kassie.Makeover.Models;

namespace Kassie.Makeover.Services;

public sealed class HairReferenceAnalysisService
{
    private readonly HairSegmentationService _segmentation;

    public HairReferenceAnalysisService(HairSegmentationService segmentation)
    {
        _segmentation = segmentation;
    }

    public string? Analyse(string referencePath)
    {
        if (string.IsNullOrWhiteSpace(referencePath) || !File.Exists(referencePath))
            return null;

        var id = Guid.NewGuid().ToString("N");
        var root = Path.Combine(AppPaths.Temp, $"hair-reference-{id}");
        Directory.CreateDirectory(root);

        try
        {
            var segmentation = _segmentation.CreateMasks(
                referencePath,
                new HairSettings { Length = "Medium" },
                root);

            using var source = Cv2.ImRead(referencePath, ImreadModes.Color);
            using var mask = Cv2.ImRead(segmentation.HairMaskPath, ImreadModes.Grayscale);

            if (source.Empty() || mask.Empty())
                return "use the selected reference for overall haircut silhouette";

            using var resizedMask = new Mat();
            if (mask.Size() != source.Size())
                Cv2.Resize(mask, resizedMask, source.Size(), 0, 0, InterpolationFlags.Nearest);
            else
                mask.CopyTo(resizedMask);

            if (Cv2.CountNonZero(resizedMask) < 20)
                return "use the selected reference for overall haircut silhouette";

            var rect = Cv2.BoundingRect(resizedMask);
            var verticalRatio = rect.Height / (double)Math.Max(source.Height, 1);
            var length = verticalRatio switch
            {
                < 0.24 => "short",
                < 0.40 => "medium-length",
                < 0.62 => "long",
                _ => "very long"
            };

            var mean = Cv2.Mean(source, resizedMask);
            var brightness = (mean.Val0 + mean.Val1 + mean.Val2) / 3.0;
            var colour = brightness switch
            {
                < 55 => "very dark",
                < 105 => "dark",
                < 165 => "medium-toned",
                _ => "light"
            };

            var widthRatio = rect.Width / (double)Math.Max(source.Width, 1);
            var volume = widthRatio switch
            {
                < 0.32 => "sleek/low-volume",
                < 0.52 => "natural-volume",
                _ => "full/high-volume"
            };

            return $"{length}, {volume}, {colour} hair; use the reference mainly for silhouette, length and volume";
        }
        catch (Exception ex)
        {
            AppLog.HairAi($"Reference analysis failed for {referencePath}: {ex.Message}");
            return "use the selected reference for overall haircut silhouette";
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
