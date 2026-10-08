namespace Kassie.Makeover.Models;

public sealed record HairSettings
{
    public bool Enabled { get; init; } = true;
    public int Intensity { get; init; } = 32;
    public string Color { get; init; } = "#6B4636";
    public string Finish { get; init; } = "Natural";
    public string Coverage { get; init; } = "Full";
    public string StyleFamily { get; init; } = "Layered";
    public string Length { get; init; } = "Medium";
    public string Texture { get; init; } = "Wavy";
    public string Fringe { get; init; } = "None";
    public string Volume { get; init; } = "Natural";
    public string StrengthHint { get; init; } = "Balanced";
    public string QualityHint { get; init; } = "Normal";
    public string? ReferencePath { get; init; }
}
