namespace Kassie.Makeover.Models;

public sealed class HairGenerationResult
{
    public Guid RequestId { get; set; }
    public DateTime CompletedAt { get; set; } = DateTime.Now;
    public string OutputPath { get; set; } = "";
    public string Model { get; set; } = "";
    public string Prompt { get; set; } = "";
}
