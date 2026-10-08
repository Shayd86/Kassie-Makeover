using System.Globalization;
using OpenCvSharp;
using Kassie.Makeover.Models;

namespace Kassie.Makeover.Services;

public sealed class HairService
{
    public void Apply(Mat frame, HairSettings settings, Rect? face)
    {
        if (!settings.Enabled || face is not Rect f || f.Width < 40 || f.Height < 40 || frame.Empty())
            return;

        var outer = new Rect(
            f.X - (int)(f.Width * 0.34),
            f.Y - (int)(f.Height * 0.70),
            (int)(f.Width * 1.68),
            (int)(f.Height * 1.34));

        outer = Clamp(outer, frame.Width, frame.Height);
        if (outer.Width < 10 || outer.Height < 10)
            return;

        using var target = new Mat(frame, outer);
        using var mask = new Mat(target.Rows, target.Cols, MatType.CV_8UC1, Scalar.Black);

        var faceCenterX = f.X + f.Width / 2 - outer.X;
        var faceTopY = f.Y - outer.Y;

        var outerCenter = new Point(
            faceCenterX,
            faceTopY + (int)(f.Height * 0.16));

        var outerAxes = new Size(
            Math.Max(12, (int)(f.Width * 0.76)),
            Math.Max(14, (int)(f.Height * 0.77)));

        Cv2.Ellipse(mask, outerCenter, outerAxes, 0, 0, 360, Scalar.White, -1, LineTypes.AntiAlias);

        // Cut out the central face area, leaving crown and side hair.
        var faceCutCenter = new Point(
            faceCenterX,
            faceTopY + (int)(f.Height * 0.53));

        var faceCutAxes = new Size(
            Math.Max(10, (int)(f.Width * 0.49)),
            Math.Max(12, (int)(f.Height * 0.60)));

        Cv2.Ellipse(mask, faceCutCenter, faceCutAxes, 0, 0, 360, Scalar.Black, -1, LineTypes.AntiAlias);

        // Preserve a shallow fringe/hairline band.
        var hairlineTop = Math.Max(0, faceTopY - (int)(f.Height * 0.08));
        var hairlineBottom = Math.Min(mask.Rows - 1, faceTopY + (int)(f.Height * 0.17));
        if (hairlineBottom > hairlineTop)
        {
            Cv2.Rectangle(
                mask,
                new Rect(
                    Math.Max(0, faceCenterX - (int)(f.Width * 0.47)),
                    hairlineTop,
                    Math.Min(mask.Cols - Math.Max(0, faceCenterX - (int)(f.Width * 0.47)), (int)(f.Width * 0.94)),
                    hairlineBottom - hairlineTop),
                Scalar.White,
                -1);
        }

        switch (settings.Coverage)
        {
            case "Roots":
                using (var coverage = new Mat(mask.Rows, mask.Cols, MatType.CV_8UC1, Scalar.Black))
                {
                    var h = Math.Min(mask.Rows, Math.Max(8, faceTopY + (int)(f.Height * 0.25)));
                    Cv2.Rectangle(coverage, new Rect(0, 0, mask.Cols, h), Scalar.White, -1);
                    Cv2.BitwiseAnd(mask, coverage, mask);
                }
                break;

            case "Highlights":
                using (var stripes = new Mat(mask.Rows, mask.Cols, MatType.CV_8UC1, Scalar.Black))
                {
                    var step = Math.Max(12, f.Width / 7);
                    var thickness = Math.Max(4, step / 3);
                    for (var x = -mask.Rows; x < mask.Cols + mask.Rows; x += step)
                        Cv2.Line(stripes, new Point(x, 0), new Point(x + mask.Rows / 2, mask.Rows), Scalar.White, thickness, LineTypes.AntiAlias);

                    Cv2.GaussianBlur(stripes, stripes, new Size(0, 0), Math.Max(2.0, f.Width * 0.015));
                    Cv2.BitwiseAnd(mask, stripes, mask);
                }
                break;
        }

        // Feather heavily so it reads as a tint and preserves actual hair texture.
        Cv2.GaussianBlur(mask, mask, new Size(0, 0), Math.Max(4.0, f.Width * 0.025));

        var intensity = Math.Clamp(settings.Intensity, 0, 100) / 100.0;
        var alpha = settings.Finish switch
        {
            "Glossy" => 0.10 + intensity * 0.25,
            "Matte" => 0.12 + intensity * 0.28,
            _ => 0.08 + intensity * 0.22
        };

        BlendColour(target, mask, ParseHex(settings.Color), alpha);

        if (settings.Finish == "Glossy")
        {
            using var shine = new Mat(mask.Rows, mask.Cols, MatType.CV_8UC1, Scalar.Black);
            Cv2.Ellipse(
                shine,
                new Point(outerCenter.X - (int)(f.Width * 0.16), outerCenter.Y - (int)(f.Height * 0.20)),
                new Size(Math.Max(8, (int)(f.Width * 0.30)), Math.Max(5, (int)(f.Height * 0.12))),
                -15,
                185,
                330,
                Scalar.White,
                -1,
                LineTypes.AntiAlias);
            Cv2.GaussianBlur(shine, shine, new Size(0, 0), Math.Max(5.0, f.Width * 0.03));
            Cv2.BitwiseAnd(shine, mask, shine);
            BlendColour(target, shine, new Scalar(230, 230, 235), 0.06 + intensity * 0.05);
        }
    }

    private static Rect Clamp(Rect r, int width, int height)
    {
        var x = Math.Clamp(r.X, 0, Math.Max(0, width - 1));
        var y = Math.Clamp(r.Y, 0, Math.Max(0, height - 1));
        var w = Math.Clamp(r.Width, 1, Math.Max(1, width - x));
        var h = Math.Clamp(r.Height, 1, Math.Max(1, height - y));
        return new Rect(x, y, w, h);
    }

    private static void BlendColour(Mat target, Mat mask8, Scalar color, double strength)
    {
        strength = Math.Clamp(strength, 0.0, 0.75);
        if (strength <= 0.001 || target.Empty())
            return;

        using var tinted = new Mat(target.Size(), target.Type(), color);
        using var tintWeight = new Mat();
        using var baseWeight = new Mat();

        mask8.ConvertTo(tintWeight, MatType.CV_32FC1, strength / 255.0);
        baseWeight.Create(tintWeight.Size(), MatType.CV_32FC1);
        baseWeight.SetTo(Scalar.All(1.0));
        Cv2.Subtract(baseWeight, tintWeight, baseWeight);

        Cv2.BlendLinear(tinted, target, tintWeight, baseWeight, target);
    }

    private static Scalar ParseHex(string hex)
    {
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6) return new Scalar(70, 70, 70);

        var r = byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Scalar(b, g, r);
    }
}
