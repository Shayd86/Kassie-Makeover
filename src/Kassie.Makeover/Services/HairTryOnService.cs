using System.IO;
using System.Text.Json;
using Kassie.Makeover.Infrastructure;
using Kassie.Makeover.Models;

namespace Kassie.Makeover.Services;

public sealed class HairTryOnService
{
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    public string ImportSource(string sourcePath)
    {
        AppPaths.Ensure();

        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not (".jpg" or ".jpeg" or ".png" or ".bmp"))
            throw new InvalidOperationException("Choose a JPG, PNG or BMP image.");

        var target = Path.Combine(
            AppPaths.HairInputs,
            $"Hair-Source-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}{extension}");

        File.Copy(sourcePath, target, false);
        return target;
    }

    public HairTryOnRequest Prepare(string sourcePath, HairSettings settings)
    {
        AppPaths.Ensure();

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("The hairstyle source photo cannot be found.", sourcePath);

        if (!string.IsNullOrWhiteSpace(settings.ReferencePath) && !File.Exists(settings.ReferencePath))
            settings = settings with { ReferencePath = null };

        var request = new HairTryOnRequest
        {
            SourcePath = sourcePath,
            ReferencePath = settings.ReferencePath,
            Settings = settings,
            Prompt = BuildPrompt(settings),
            Status = "Prepared"
        };

        var requestPath = Path.Combine(AppPaths.HairRequests, $"{request.Id:N}.json");
        File.WriteAllText(requestPath, JsonSerializer.Serialize(request, _json));

        AppLog.Write($"Prepared hairstyle request {request.Id:N}: {request.Prompt}");
        return request;
    }

    public static string BuildPrompt(HairSettings settings)
    {
        var fringe = settings.Fringe.Equals("None", StringComparison.OrdinalIgnoreCase)
            ? "no fringe"
            : $"{settings.Fringe.ToLowerInvariant()} fringe";

        return
            $"{settings.StyleFamily} hairstyle, {settings.Length.ToLowerInvariant()} length, " +
            $"{settings.Texture.ToLowerInvariant()} texture, {fringe}, " +
            $"{settings.Volume.ToLowerInvariant()} volume. Preserve the person's identity, face, " +
            "expression, skin, clothing, pose, lighting and background; change only the hair.";
    }
}
