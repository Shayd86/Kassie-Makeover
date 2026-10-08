namespace Kassie.Makeover.Models;

public sealed class WardrobeItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Wardrobe item";
    public string FilePath { get; set; } = "";
    public DateTime AddedAt { get; set; } = DateTime.Now;
}
