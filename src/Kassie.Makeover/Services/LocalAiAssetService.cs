using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Kassie.Makeover.Infrastructure;

namespace Kassie.Makeover.Services;

public sealed class LocalAiAssetService
{
    private const string RuntimeTag = "master-946-a4a9669";

    private const string VulkanZipUrl =
        "https://github.com/leejet/stable-diffusion.cpp/releases/download/" + RuntimeTag + "/sd-master-a4a9669-bin-win-vulkan-x64.zip";
    private const string VulkanZipSha256 =
        "61100708262a0152f41b938ed30af97533662e94c54b60b83a8f52982dc1b2a8";

    private const string CpuZipUrl =
        "https://github.com/leejet/stable-diffusion.cpp/releases/download/" + RuntimeTag + "/sd-master-a4a9669-bin-win-cpu-x64.zip";
    private const string CpuZipSha256 =
        "c8aafbb3afc4f3cd47c723748da41417b19a300f8ed9d6ab09d142ae14456092";

    private const string ModelUrl =
        "https://huggingface.co/second-state/stable-diffusion-v1-5-GGUF/resolve/main/stable-diffusion-v1-5-pruned-emaonly-Q4_0.gguf?download=true";
    private const string ModelSha256 =
        "b8944e9fe0b69b36ae1b5bb0185b3a7b8ef14347fe0fa9af6c64c4829022261f";

    private const string HairSegmenterUrl =
        "https://huggingface.co/senty-au/selfie_multiclass_256x256-ONNX/resolve/main/onnx/model.onnx?download=true";
    private const string HairSegmenterSha256 =
        "35ec1ecd9ee7f85073c99c00020b7f6751b69506eeacf683bc8665f6117f85b0";

    private static readonly HttpClient Client = CreateClient();

    public string RuntimeRoot => Path.Combine(AppPaths.Runtimes, "stable-diffusion.cpp");
    public string VulkanRoot => Path.Combine(RuntimeRoot, "vulkan");
    public string CpuRoot => Path.Combine(RuntimeRoot, "cpu");
    public string ModelPath => Path.Combine(AppPaths.HairModels, "sd15-q4_0.gguf");
    public string SegmenterPath => Path.Combine(AppPaths.HairModels, "selfie_multiclass_256.onnx");

    public bool IsReady =>
        File.Exists(ModelPath) &&
        File.Exists(SegmenterPath) &&
        FindSdCli(VulkanRoot) is not null &&
        FindSdCli(CpuRoot) is not null;

    public async Task EnsureReadyAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        AppPaths.Ensure();
        Directory.CreateDirectory(RuntimeRoot);
        Directory.CreateDirectory(AppPaths.HairModels);

        await EnsureRuntimeAsync(
            "Vulkan",
            VulkanZipUrl,
            VulkanZipSha256,
            VulkanRoot,
            progress,
            cancellationToken);

        await EnsureRuntimeAsync(
            "CPU fallback",
            CpuZipUrl,
            CpuZipSha256,
            CpuRoot,
            progress,
            cancellationToken);

        await EnsureFileAsync(
            "local hairstyle model (about 1.6 GB)",
            ModelUrl,
            ModelSha256,
            ModelPath,
            progress,
            cancellationToken);

        await EnsureFileAsync(
            "hair segmentation model",
            HairSegmenterUrl,
            HairSegmenterSha256,
            SegmenterPath,
            progress,
            cancellationToken);

        progress?.Report("Local AI ready • no API key and no per-image charge");
        AppLog.Write("Local hairstyle AI assets are ready.");
    }

    public string? GetVulkanCli() => FindSdCli(VulkanRoot);
    public string? GetCpuCli() => FindSdCli(CpuRoot);

    private async Task EnsureRuntimeAsync(
        string label,
        string url,
        string sha256,
        string extractRoot,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var existing = FindSdCli(extractRoot);
        if (existing is not null)
            return;

        var archive = Path.Combine(RuntimeRoot, label.StartsWith("Vulkan") ? "vulkan.zip" : "cpu.zip");

        await EnsureFileAsync(
            $"{label} local AI runtime",
            url,
            sha256,
            archive,
            progress,
            cancellationToken);

        progress?.Report($"Installing {label} local AI runtime…");

        if (Directory.Exists(extractRoot))
            Directory.Delete(extractRoot, true);

        Directory.CreateDirectory(extractRoot);
        ZipFile.ExtractToDirectory(archive, extractRoot, true);

        if (FindSdCli(extractRoot) is null)
            throw new InvalidOperationException($"{label} runtime downloaded but sd-cli.exe was not found.");

        try { File.Delete(archive); } catch { }
    }

    private static string? FindSdCli(string root)
    {
        if (!Directory.Exists(root))
            return null;

        return Directory
            .EnumerateFiles(root, "sd-cli.exe", SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    private async Task EnsureFileAsync(
        string label,
        string url,
        string sha256,
        string targetPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (File.Exists(targetPath))
        {
            progress?.Report($"Checking {label}…");
            if (await HashMatchesAsync(targetPath, sha256, cancellationToken))
                return;

            try { File.Delete(targetPath); } catch { }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var partPath = targetPath + ".part";
        var existingBytes = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existingBytes > 0)
            request.Headers.Range = new RangeHeaderValue(existingBytes, null);

        progress?.Report(existingBytes > 0
            ? $"Resuming {label} at {FormatBytes(existingBytes)}…"
            : $"Downloading {label}…");

        using var response = await Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (existingBytes > 0 && response.StatusCode == HttpStatusCode.OK)
        {
            existingBytes = 0;
            try { File.Delete(partPath); } catch { }
        }

        response.EnsureSuccessStatusCode();

        var total =
            response.Content.Headers.ContentRange?.Length ??
            (response.Content.Headers.ContentLength is long len ? len + existingBytes : (long?)null);

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(
            partPath,
            existingBytes > 0 ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            true);

        var buffer = new byte[1024 * 1024];
        var downloaded = existingBytes;
        var lastReport = DateTime.UtcNow;

        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloaded += read;

            if ((DateTime.UtcNow - lastReport).TotalMilliseconds >= 600)
            {
                progress?.Report(total is > 0
                    ? $"Downloading {label} • {FormatBytes(downloaded)} / {FormatBytes(total.Value)}"
                    : $"Downloading {label} • {FormatBytes(downloaded)}");
                lastReport = DateTime.UtcNow;
            }
        }

        await destination.FlushAsync(cancellationToken);
        destination.Close();

        progress?.Report($"Verifying {label}…");

        if (!await HashMatchesAsync(partPath, sha256, cancellationToken))
        {
            try { File.Delete(partPath); } catch { }
            throw new InvalidOperationException($"{label} downloaded but failed its SHA-256 check.");
        }

        File.Move(partPath, targetPath, true);
    }

    private static async Task<bool> HashMatchesAsync(
        string path,
        string expected,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            true);

        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        var value = (double)bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.0} {units[unit]}";
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = Timeout.InfiniteTimeSpan
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("Kassie-Makeover/0.8.1");
        return client;
    }
}
