using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Kassie.Makeover.Infrastructure;
using Kassie.Makeover.Models;

namespace Kassie.Makeover.Services;

public sealed class HairGenerationService
{
    private const string Endpoint = "https://api.openai.com/v1/images/edits";
    private const string Model = "gpt-image-1.5";

    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(8)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Kassie-Makeover/0.8.0");
        return client;
    }

    public async Task<HairGenerationResult> GenerateAsync(
        HairTryOnRequest request,
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("An OpenAI API key is required for hairstyle generation.");

        if (!File.Exists(request.SourcePath))
            throw new FileNotFoundException("The hairstyle source image cannot be found.", request.SourcePath);

        AppPaths.Ensure();

        var prompt = BuildGenerationPrompt(request);
        using var form = new MultipartFormDataContent();

        AddText(form, "model", Model);
        AddText(form, "prompt", prompt);
        AddText(form, "input_fidelity", "high");
        AddText(form, "quality", "high");
        AddText(form, "size", "auto");
        AddText(form, "output_format", "png");

        await AddImageAsync(form, request.SourcePath, cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.ReferencePath) && File.Exists(request.ReferencePath))
            await AddImageAsync(form, request.ReferencePath, cancellationToken);

        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = form
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

        AppLog.Write($"Generating hairstyle request {request.Id:N} with {Model}.");

        using var response = await Client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var detail = ExtractApiError(body);
            AppLog.Write($"Hairstyle generation API error {(int)response.StatusCode}: {detail}");
            throw new InvalidOperationException(
                $"Hairstyle generation failed ({(int)response.StatusCode}): {detail}");
        }

        using var json = JsonDocument.Parse(body);
        if (!json.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array ||
            data.GetArrayLength() == 0 ||
            !data[0].TryGetProperty("b64_json", out var imageElement))
        {
            throw new InvalidOperationException("The hairstyle service returned no image.");
        }

        var base64 = imageElement.GetString();
        if (string.IsNullOrWhiteSpace(base64))
            throw new InvalidOperationException("The hairstyle service returned an empty image.");

        byte[] imageBytes;
        try
        {
            imageBytes = Convert.FromBase64String(base64);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("The hairstyle service returned invalid image data.", ex);
        }

        var outputPath = Path.Combine(
            AppPaths.HairOutputs,
            $"Hair-Result-{DateTime.Now:yyyyMMdd-HHmmss}-{request.Id:N}.png");

        await File.WriteAllBytesAsync(outputPath, imageBytes, cancellationToken);

        var result = new HairGenerationResult
        {
            RequestId = request.Id,
            OutputPath = outputPath,
            Model = Model,
            Prompt = prompt,
            CompletedAt = DateTime.Now
        };

        var resultPath = Path.Combine(AppPaths.HairRequests, $"{request.Id:N}-result.json");
        await File.WriteAllTextAsync(
            resultPath,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        AppLog.Write($"Hairstyle result saved: {outputPath}");
        return result;
    }

    private static string BuildGenerationPrompt(HairTryOnRequest request)
    {
        var referenceInstruction =
            !string.IsNullOrWhiteSpace(request.ReferencePath) && File.Exists(request.ReferencePath)
                ? """
                  
                  The FIRST image is the person to edit. The SECOND image is a hairstyle reference only.
                  Use the second image only for the haircut, shape, fringe, texture and volume.
                  Do not copy the second person's face, identity, skin, clothing, background or pose.
                  """
                : "";

        return $"""
                Edit the FIRST image as a photorealistic virtual hairstyle try-on.

                {request.Prompt}

                Critical requirements:
                - Keep the person recognisably the exact same person.
                - Preserve facial identity and facial features with high fidelity.
                - Preserve expression, eyes, eyebrows, nose, lips, skin, ears and visible tattoos.
                - Preserve body, clothing, pose, camera angle, lighting and background.
                - Change only the hair and the immediately necessary hairline/occlusion around it.
                - Make the new hairstyle physically believable for this head angle and lighting.
                - Hair strands, edges, shadows and highlights must look photographic, not painted or pasted on.
                - Do not beautify, age, de-age or reshape the face.
                - Do not add makeup, jewellery, hats or accessories unless already present.
                - Do not change another person visible in the background.
                {referenceInstruction}
                Return one finished photorealistic edited image.
                """;
    }

    private static void AddText(MultipartFormDataContent form, string name, string value)
        => form.Add(new StringContent(value), name);

    private static async Task AddImageAsync(
        MultipartFormDataContent form,
        string path,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(GetMimeType(path));
        form.Add(content, "image[]", Path.GetFileName(path));
    }

    private static string GetMimeType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        _ => "image/png"
    };

    private static string ExtractApiError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? "Unknown API error.";
            }
        }
        catch
        {
            // Fall through to a compact raw response.
        }

        var compact = json.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return compact.Length <= 500 ? compact : compact[..500] + "…";
    }
}
