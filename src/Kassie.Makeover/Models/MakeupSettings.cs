namespace Kassie.Makeover.Models;

public sealed record MakeupSettings
{
    public bool Enabled { get; init; } = true;
    public bool Lipstick { get; init; } = true;
    public bool Blush { get; init; } = true;
    public bool Eyeshadow { get; init; } = true;
    public int Intensity { get; init; } = 45;
    public string LipColor { get; init; } = "#D84D72";
    public string BlushColor { get; init; } = "#E6758E";
    public string EyeColor { get; init; } = "#8A5A7A";
}
