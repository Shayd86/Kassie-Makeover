namespace Kassie.Makeover.Models;

public sealed record FaceAnalysisSnapshot(
    string FaceShape,
    string EyeSpacing,
    string FeatureBalance,
    double FaceAspectRatio,
    double EyeSpacingRatio,
    double MouthWidthRatio,
    string ConfidenceNote);
