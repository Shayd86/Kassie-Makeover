using System.IO;
using System.Text.Json;
using Kassie.Makeover.Infrastructure;
using Kassie.Makeover.Models;

namespace Kassie.Makeover.Services;

public sealed class LookPresetService
{
    private readonly string _path = Path.Combine(AppPaths.Config, "looks.json");
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public List<LookPreset> Load()
    {
        AppPaths.Ensure();
        try
        {
            if (!File.Exists(_path)) return [];
            return JsonSerializer.Deserialize<List<LookPreset>>(File.ReadAllText(_path), _json) ?? [];
        }
        catch (Exception ex)
        {
            AppLog.Write($"Could not load look presets: {ex}");
            return [];
        }
    }

    public void Save(List<LookPreset> looks)
    {
        AppPaths.Ensure();
        File.WriteAllText(_path, JsonSerializer.Serialize(looks, _json));
    }
}
