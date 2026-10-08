using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using OpenCvSharp;
using Kassie.Makeover.Infrastructure;
using Kassie.Makeover.Models;

namespace Kassie.Makeover.Services;

public sealed class HairGenerationService
{
    private readonly LocalAiAssetService _assets;
    private readonly HairSegmentationService _segmentation;
    private readonly HairReferenceAnalysisService _referenceAnalysis;

    public HairGenerationService(
        LocalAiAssetService assets,
        HairSegmentationService segmentation,
        HairReferenceAnalysisService referenceAnalysis)
    {
        _assets = assets;
        _segmentation = segmentation;
        _referenceAnalysis = referenceAnalysis;
    }

    public async Task<HairGenerationResult> GenerateAsync(
        HairTryOnRequest request,
        long seed,
        Guid? batchId = null,
        Guid? parentResultId = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(request.SourcePath))
            throw new FileNotFoundException("The hairstyle source image cannot be found.", request.SourcePath);

        await _assets.EnsureReadyAsync(progress, cancellationToken);

        var options = new HairGenerationOptions
        {
            StrengthPreset = request.Settings.StrengthHint,
            QualityPreset = request.Settings.QualityHint
        };

        var resultId = Guid.NewGuid();
        var workId = request.Id.ToString("N");
        var workingSource = Path.Combine(AppPaths.Temp, $"hair-source-{workId}.png");
        var diagnosticsDirectory = Path.Combine(AppPaths.HairHistory, resultId.ToString("N"));
        var outputPath = Path.Combine(
            AppPaths.HairOutputs,
            $"Hair-Result-{DateTime.Now:yyyyMMdd-HHmmss}-{resultId:N}.png");

        Directory.CreateDirectory(diagnosticsDirectory);

        try
        {
            progress?.Report("Preparing portrait for local AI…");
            var size = await Task.Run(
                () => PrepareWorkingImage(request.SourcePath, workingSource, options.MaxSide),
                cancellationToken);

            progress?.Report("Finding hair, face and editable regions…");
            var masks = await Task.Run(
                () => _segmentation.CreateMasks(
                    workingSource,
                    request.Settings,
                    diagnosticsDirectory,
                    "generation"),
                cancellationToken);

            string? referenceGuidance = null;
            if (!string.IsNullOrWhiteSpace(request.ReferencePath) &&
                File.Exists(request.ReferencePath))
            {
                progress?.Report("Analysing hairstyle reference locally…");
                referenceGuidance = await Task.Run(
                    () => _referenceAnalysis.Analyse(request.ReferencePath),
                    cancellationToken);
            }

            progress?.Report("Generating hairstyle locally • GPU first…");

            var prompt = BuildGenerationPrompt(request, referenceGuidance);
            var negative =
                "different person, changed face, deformed face, distorted eyes, distorted mouth, " +
                "extra face, extra person, hat, headwear, helmet, obvious wig, wig edge, plastic hair, " +
                "painted hair, cartoon, illustration, low quality, blurry, bad anatomy, changed clothing, " +
                "changed background, changed skin tone";

            var vulkan = _assets.GetVulkanCli();
            var cpu = _assets.GetCpuCli();

            if (vulkan is null && cpu is null)
                throw new InvalidOperationException("The local hairstyle runtime is missing.");

            var error = "";

            if (vulkan is not null)
            {
                var result = await RunSdCliAsync(
                    vulkan,
                    useGpu: true,
                    workingSource,
                    masks.EditMaskPath,
                    outputPath,
                    prompt,
                    negative,
                    size.Width,
                    size.Height,
                    options,
                    seed,
                    progress,
                    cancellationToken);

                if (result.ExitCode == 0 && File.Exists(outputPath))
                {
                    return await FinishAsync(
                        resultId,
                        request,
                        outputPath,
                        prompt,
                        "stable-diffusion.cpp Vulkan / SD1.5 Q4",
                        options,
                        seed,
                        batchId,
                        parentResultId,
                        masks,
                        cancellationToken);
                }

                error = result.ErrorText;
                AppLog.HairAi($"Vulkan hairstyle generation failed; trying CPU fallback. {error}");
                progress?.Report("GPU path failed • retrying locally on CPU…");
            }

            if (cpu is not null)
            {
                try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }

                var result = await RunSdCliAsync(
                    cpu,
                    useGpu: false,
                    workingSource,
                    masks.EditMaskPath,
                    outputPath,
                    prompt,
                    negative,
                    size.Width,
                    size.Height,
                    options,
                    seed,
                    progress,
                    cancellationToken);

                if (result.ExitCode == 0 && File.Exists(outputPath))
                {
                    return await FinishAsync(
                        resultId,
                        request,
                        outputPath,
                        prompt,
                        "stable-diffusion.cpp CPU / SD1.5 Q4",
                        options,
                        seed,
                        batchId,
                        parentResultId,
                        masks,
                        cancellationToken);
                }

                error = result.ErrorText;
            }

            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error)
                    ? "The local hairstyle engine stopped without creating an image."
                    : $"The local hairstyle engine failed: {LastUsefulLines(error)}");
        }
        catch
        {
            AppLog.HairAi($"Generation {resultId:N} failed.");
            throw;
        }
        finally
        {
            try { if (File.Exists(workingSource)) File.Delete(workingSource); } catch { }
        }
    }

    private static Size PrepareWorkingImage(string sourcePath, string outputPath, int maxSide)
    {
        using var source = Cv2.ImRead(sourcePath, ImreadModes.Color);
        if (source.Empty())
            throw new InvalidOperationException("The source portrait could not be opened.");

        var scale = Math.Min(1.0, maxSide / (double)Math.Max(source.Width, source.Height));

        var scaledWidth = Math.Max(256, (int)Math.Round(source.Width * scale));
        var scaledHeight = Math.Max(256, (int)Math.Round(source.Height * scale));

        var width = Math.Max(256, (scaledWidth / 64) * 64);
        var height = Math.Max(256, (scaledHeight / 64) * 64);

        width = Math.Min(maxSide, width);
        height = Math.Min(maxSide, height);

        using var resized = new Mat();
        Cv2.Resize(source, resized, new Size(width, height), 0, 0, InterpolationFlags.Lanczos4);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        if (!Cv2.ImWrite(outputPath, resized))
            throw new IOException("The local AI working portrait could not be saved.");

        return new Size(width, height);
    }

    private static async Task<(int ExitCode, string ErrorText)> RunSdCliAsync(
        string executable,
        bool useGpu,
        string source,
        string mask,
        string output,
        string prompt,
        string negativePrompt,
        int width,
        int height,
        HairGenerationOptions options,
        long seed,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        void Add(string value) => psi.ArgumentList.Add(value);

        Add("-m"); Add(Path.GetFullPath(Path.Combine(AppPaths.HairModels, "sd15-q4_0.gguf")));
        Add("-i"); Add(source);
        Add("--mask"); Add(mask);
        Add("-o"); Add(output);
        Add("-p"); Add(prompt);
        Add("-n"); Add(negativePrompt);
        Add("--seed"); Add(seed.ToString());
        Add("--strength"); Add(options.Strength.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        Add("--steps"); Add(options.Steps.ToString());
        Add("--cfg-scale"); Add("6.5");
        Add("--sampling-method"); Add("euler_a");
        Add("--width"); Add(width.ToString());
        Add("--height"); Add(height.ToString());
        Add("--vae-tiling");
        Add("--vae-on-cpu");
        Add("--clip-on-cpu");
        Add("--offload-to-cpu");
        Add("--auto-fit"); Add("on");

        if (useGpu)
        {
            Add("--backend"); Add("diffusion=vulkan0,vae=cpu,te=cpu");
            Add("--max-vram"); Add("vulkan0=3.2");
        }
        else
        {
            Add("--backend"); Add("cpu");
        }

        using var process = new Process { StartInfo = psi };
        var log = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            lock (log) log.AppendLine(e.Data);
            ReportInterestingProgress(e.Data, progress);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrWhiteSpace(e.Data)) return;
            lock (log) log.AppendLine(e.Data);
            ReportInterestingProgress(e.Data, progress);
        };

        AppLog.HairAi(
            $"Starting {Path.GetFileName(executable)} {(useGpu ? "GPU" : "CPU")} " +
            $"seed={seed} strength={options.Strength:0.00} steps={options.Steps} size={width}x{height}");

        if (!process.Start())
            throw new InvalidOperationException("The local hairstyle process could not be started.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(true);
            }
            catch { }

            throw;
        }

        string text;
        lock (log) text = log.ToString();

        AppLog.HairAi($"Local hairstyle runtime exited {process.ExitCode}. {LastUsefulLines(text)}");
        return (process.ExitCode, text);
    }

    private static void ReportInterestingProgress(string line, IProgress<string>? progress)
    {
        var lower = line.ToLowerInvariant();

        if (lower.Contains("generating image") || lower.Contains("sampling") || lower.Contains("step "))
            progress?.Report("Generating hairstyle locally…");
        else if (lower.Contains("loading") && lower.Contains("model"))
            progress?.Report("Loading local hairstyle model…");
        else if (lower.Contains("vae"))
            progress?.Report("Finishing hairstyle image…");
    }

    private static async Task<HairGenerationResult> FinishAsync(
        Guid resultId,
        HairTryOnRequest request,
        string outputPath,
        string prompt,
        string model,
        HairGenerationOptions options,
        long seed,
        Guid? batchId,
        Guid? parentResultId,
        HairSegmentationResult masks,
        CancellationToken cancellationToken)
    {
        var result = new HairGenerationResult
        {
            Id = resultId,
            RequestId = request.Id,
            BatchId = batchId,
            ParentResultId = parentResultId,
            SourcePath = request.SourcePath,
            OutputPath = outputPath,
            ReferencePath = request.ReferencePath,
            Settings = request.Settings,
            Seed = seed,
            Model = model,
            Prompt = prompt,
            StrengthPreset = options.StrengthPreset,
            QualityPreset = options.QualityPreset,
            HairMaskPath = masks.HairMaskPath,
            FaceMaskPath = masks.FaceMaskPath,
            EditMaskPath = masks.EditMaskPath,
            CompletedAt = DateTime.Now
        };

        var resultPath = Path.Combine(AppPaths.HairRequests, $"{request.Id:N}-result.json");

        await File.WriteAllTextAsync(
            resultPath,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        AppLog.HairAi($"Local hairstyle result saved: {outputPath}");
        return result;
    }

    private static string BuildGenerationPrompt(
        HairTryOnRequest request,
        string? referenceGuidance)
    {
        var settings = request.Settings;
        var fringe = settings.Fringe.Equals("None", StringComparison.OrdinalIgnoreCase)
            ? "no fringe"
            : $"{settings.Fringe.ToLowerInvariant()} fringe";

        var reference = string.IsNullOrWhiteSpace(referenceGuidance)
            ? ""
            : $", hairstyle reference guidance: {referenceGuidance}";

        return
            "photorealistic portrait photo, same exact person, same facial identity and same face, " +
            $"new {settings.StyleFamily.ToLowerInvariant()} hairstyle, " +
            $"{settings.Length.ToLowerInvariant()} length, {settings.Texture.ToLowerInvariant()} hair, " +
            $"{fringe}, {settings.Volume.ToLowerInvariant()} volume{reference}, " +
            "natural individual hair strands, believable hairline, realistic roots and flyaways, " +
            "match the original camera angle and lighting, keep eyes eyebrows nose lips expression ears " +
            "skin tone body clothing pose and background unchanged, change only the hair";
    }

    private static string LastUsefulLines(string text)
    {
        var lines = text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .TakeLast(8);

        var compact = string.Join(" | ", lines);
        return compact.Length <= 900 ? compact : compact[^900..];
    }
}
