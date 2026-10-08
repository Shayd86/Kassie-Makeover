using System.IO;
using System.Text.Json;
using Kassie.Makeover.Infrastructure;
using Kassie.Makeover.Models;

namespace Kassie.Makeover.Services;

public sealed class HairReferenceService
{
    private readonly string _indexPath = Path.Combine(AppPaths.Hair, "references.json");
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public List<HairReference> Load()
    {
        AppPaths.Ensure();
        try
        {
            if (!File.Exists(_indexPath)) return [];
            var items = JsonSerializer.Deserialize<List<HairReference>>(File.ReadAllText(_indexPath), _json) ?? [];
            return items.Where(x => File.Exists(x.FilePath)).OrderByDescending(x => x.AddedAt).ToList();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Could not load hair references: {ex}");
            return [];
        }
    }

    public HairReference Import(string sourcePath)
    {
        AppPaths.Ensure();

        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png" or ".bmp"))
            throw new InvalidOperationException("Choose a JPG, PNG or BMP image.");

        var id = Guid.NewGuid();
        var target = Path.Combine(AppPaths.HairReferences, $"{id:N}{extension}");
        File.Copy(sourcePath, target, true);

        return new HairReference
        {
            Id = id,
            Name = Path.GetFileNameWithoutExtension(sourcePath),
            FilePath = target,
            AddedAt = DateTime.Now
        };
    }

    public void Save(List<HairReference> items)
    {
        AppPaths.Ensure();
        File.WriteAllText(_indexPath, JsonSerializer.Serialize(items, _json));
    }

    public void Delete(HairReference item)
    {
        try
        {
            if (File.Exists(item.FilePath))
                File.Delete(item.FilePath);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Could not delete hair reference {item.FilePath}: {ex}");
        }
    }
}
