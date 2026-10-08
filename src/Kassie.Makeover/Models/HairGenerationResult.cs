namespace Kassie.Makeover.Models;

public sealed class HairGenerationResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public Guid? BatchId { get; set; }
    public Guid? ParentResultId { get; set; }
    public DateTime CompletedAt { get; set; } = DateTime.Now;
    public string SourcePath { get; set; } = "";
    public string OutputPath { get; set; } = "";
    public string? ReferencePath { get; set; }
    public HairSettings Settings { get; set; } = new();
    public long Seed { get; set; }
    public string Model { get; set; } = "";
    public string Prompt { get; set; } = "";
    public string StrengthPreset { get; set; } = "Balanced";
    public string QualityPreset { get; set; } = "Normal";
    public string? HairMaskPath { get; set; }
    public string? FaceMaskPath { get; set; }
    public string? EditMaskPath { get; set; }

    public string Summary =>
        $"{Settings.StyleFamily} • {Settings.Length} • {Settings.Texture} • {CompletedAt:g}";
}
