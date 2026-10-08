using System.IO;
using System.Text.Json;
using Kassie.Makeover.Infrastructure;
using Kassie.Makeover.Models;

namespace Kassie.Makeover.Services;

public sealed class WardrobeService
{
    private readonly string _indexPath = Path.Combine(AppPaths.Wardrobe, "wardrobe.json");
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public List<WardrobeItem> Load()
    {
        AppPaths.Ensure();
        try
        {
            if (!File.Exists(_indexPath)) return [];
            var items = JsonSerializer.Deserialize<List<WardrobeItem>>(File.ReadAllText(_indexPath), _json) ?? [];
            return items.Where(x => File.Exists(x.FilePath)).OrderByDescending(x => x.AddedAt).ToList();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Could not load wardrobe: {ex}");
            return [];
        }
    }

    public WardrobeItem Import(string sourcePath)
    {
        AppPaths.Ensure();
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png" or ".bmp"))
            throw new InvalidOperationException("Choose a JPG, PNG or BMP image.");

        var id = Guid.NewGuid();
        var target = Path.Combine(AppPaths.Wardrobe, $"{id:N}{extension}");
        File.Copy(sourcePath, target, true);

        return new WardrobeItem
        {
            Id = id,
            Name = Path.GetFileNameWithoutExtension(sourcePath),
            FilePath = target,
            AddedAt = DateTime.Now
        };
    }

    public void Save(List<WardrobeItem> items)
    {
        AppPaths.Ensure();
        File.WriteAllText(_indexPath, JsonSerializer.Serialize(items, _json));
    }

    public void Delete(WardrobeItem item)
    {
        try
        {
            if (File.Exists(item.FilePath))
                File.Delete(item.FilePath);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Could not delete wardrobe file {item.FilePath}: {ex}");
        }
    }
}
