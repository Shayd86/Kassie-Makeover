namespace Kassie.Makeover.Models;

public sealed record HairGenerationOptions
{
    public string StrengthPreset { get; init; } = "Balanced";
    public string QualityPreset { get; init; } = "Normal";

    public double Strength => StrengthPreset switch
    {
        "Conservative" => 0.56,
        "Strong" => 0.76,
        _ => 0.66
    };

    public int Steps => QualityPreset switch
    {
        "Fast" => 16,
        "High" => 30,
        _ => 24
    };

    public int MaxSide => QualityPreset switch
    {
        "Fast" => 512,
        "High" => 704,
        _ => 640
    };
}
