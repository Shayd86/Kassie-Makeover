namespace Kassie.Makeover.Models;

public sealed class HairSegmentationResult
{
    public string HairMaskPath { get; set; } = "";
    public string FaceMaskPath { get; set; } = "";
    public string EditMaskPath { get; set; } = "";
}
