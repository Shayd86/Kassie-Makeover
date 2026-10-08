namespace Kassie.Makeover.Models;

public sealed class LookPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled look";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public MakeupSettings Makeup { get; set; } = new();
}
