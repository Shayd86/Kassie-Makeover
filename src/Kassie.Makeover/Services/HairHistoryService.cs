using System.IO;
using System.Text.Json;
using Kassie.Makeover.Infrastructure;
using Kassie.Makeover.Models;

namespace Kassie.Makeover.Services;

public sealed class HairHistoryService
{
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public List<HairGenerationResult> Load()
    {
        AppPaths.Ensure();

        try
        {
            if (!File.Exists(AppPaths.HairHistoryIndex))
                return [];

            var results = JsonSerializer.Deserialize<List<HairGenerationResult>>(
                File.ReadAllText(AppPaths.HairHistoryIndex),
                _json) ?? [];

            return results
                .Where(x => !string.IsNullOrWhiteSpace(x.OutputPath) && File.Exists(x.OutputPath))
                .OrderByDescending(x => x.CompletedAt)
                .ToList();
        }
        catch (Exception ex)
        {
            AppLog.HairAi($"History load failed: {ex}");
            return [];
        }
    }

    public void Add(HairGenerationResult result)
    {
        var items = Load();
        items.RemoveAll(x => x.Id == result.Id);
        items.Insert(0, result);
        Save(items);
    }

    public void Remove(Guid id)
    {
        var items = Load();
        items.RemoveAll(x => x.Id == id);
        Save(items);
    }

    public void Save(IEnumerable<HairGenerationResult> results)
    {
        AppPaths.Ensure();
        Directory.CreateDirectory(AppPaths.HairHistory);
        var ordered = results.OrderByDescending(x => x.CompletedAt).ToList();
        File.WriteAllText(AppPaths.HairHistoryIndex, JsonSerializer.Serialize(ordered, _json));
    }
}
