namespace Kassie.Makeover.Models;

public sealed class HairTryOnRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string SourcePath { get; set; } = "";
    public string? ReferencePath { get; set; }
    public HairSettings Settings { get; set; } = new();
    public string Prompt { get; set; } = "";
    public string Status { get; set; } = "Prepared";
}
