using Kassie.Makeover.Models;

namespace Kassie.Makeover.Services;

public sealed class MakeupAdvisorService
{
    public MakeupRecommendation Recommend(FaceAnalysisSnapshot analysis, string vibe)
    {
        var normalizedVibe = string.IsNullOrWhiteSpace(vibe) ? "Everyday" : vibe.Trim();

        var blushPlacement = analysis.FaceShape switch
        {
            "Round-ish" => "Lifted",
            "Long-ish" => "Apples",
            _ => "Lifted"
        };

        var eyeStyle = analysis.EyeSpacing switch
        {
            "Close-set" => "Outer lift",
            "Wide-set" => "Soft smoky",
            _ => "Soft wash"
        };

        var settings = normalizedVibe switch
        {
            "Soft glam" => new MakeupSettings
            {
                Intensity = 42,
                LipColor = "#B65C72",
                BlushColor = "#D97D87",
                EyeColor = "#8B654C",
                LipFinish = "Satin",
                BlushPlacement = blushPlacement,
                EyeStyle = eyeStyle == "Soft wash" ? "Soft smoky" : eyeStyle
            },
            "Evening" => new MakeupSettings
            {
                Intensity = 55,
                LipColor = "#9D3158",
                BlushColor = "#C66A7D",
                EyeColor = "#5F6070",
                LipFinish = "Satin",
                BlushPlacement = blushPlacement,
                EyeStyle = analysis.EyeSpacing == "Close-set" ? "Outer lift" : "Soft smoky"
            },
            "Bold" => new MakeupSettings
            {
                Intensity = 68,
                LipColor = "#D5283E",
                BlushColor = "#E06A87",
                EyeColor = "#773B67",
                LipFinish = "Gloss",
                BlushPlacement = "Lifted",
                EyeStyle = "Outer lift"
            },
            _ => new MakeupSettings
            {
                Intensity = 30,
                LipColor = "#B96F72",
                BlushColor = "#E89472",
                EyeColor = "#71544A",
                LipFinish = "Tint",
                BlushPlacement = blushPlacement,
                EyeStyle = eyeStyle
            }
        };

        var eyeReason = analysis.EyeSpacing switch
        {
            "Close-set" => "keeping the strongest eye colour toward the outer corners helps visually open the eye area",
            "Wide-set" => "keeping some colour through the centre of the lids helps the eye area feel connected",
            _ => "your eye spacing reads as balanced, so a soft wash is a flexible starting point"
        };

        var faceReason = analysis.FaceShape switch
        {
            "Round-ish" => "a slightly lifted blush placement adds upward structure",
            "Long-ish" => "blush kept closer to the apples avoids visually lengthening the face further",
            _ => "a gently lifted blush placement suits the balanced proportions the camera is seeing"
        };

        return new MakeupRecommendation(
            $"{normalizedVibe} suggestion",
            $"{settings.LipFinish} lip • {settings.BlushPlacement.ToLowerInvariant()} blush • {settings.EyeStyle.ToLowerInvariant()} eyes",
            $"Based on this rough camera estimate, {faceReason}; {eyeReason}. Treat this as a starting point rather than a rule.",
            settings);
    }
}
