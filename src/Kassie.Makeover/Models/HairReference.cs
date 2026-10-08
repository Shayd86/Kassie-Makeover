namespace Kassie.Makeover.Models;

public sealed class HairReference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Hair reference";
    public string FilePath { get; set; } = "";
    public DateTime AddedAt { get; set; } = DateTime.Now;
}
