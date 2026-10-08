using System.IO;
using System.Net.Http;
using Kassie.Makeover.Infrastructure;

namespace Kassie.Makeover.Services;

public sealed class ModelAssetService
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Kassie-Makeover/0.4.0");
        return client;
    }

    private static readonly (string FileName, string Url)[] MakeupAssets =
    [
        ("haarcascade_frontalface_default.xml", "https://raw.githubusercontent.com/opencv/opencv/4.x/data/haarcascades/haarcascade_frontalface_default.xml"),
        ("haarcascade_eye_tree_eyeglasses.xml", "https://raw.githubusercontent.com/opencv/opencv/4.x/data/haarcascades/haarcascade_eye_tree_eyeglasses.xml"),
        ("haarcascade_smile.xml", "https://raw.githubusercontent.com/opencv/opencv/4.x/data/haarcascades/haarcascade_smile.xml")
    ];

    public async Task EnsureMakeupAssetsAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(AppPaths.Models);

        foreach (var asset in MakeupAssets)
        {
            var target = Path.Combine(AppPaths.Models, asset.FileName);
            if (File.Exists(target) && new FileInfo(target).Length > 1000)
                continue;

            progress?.Report($"Downloading {FriendlyName(asset.FileName)}…");
            AppLog.Write($"Downloading makeup detector asset: {asset.Url}");

            var bytes = await Client.GetByteArrayAsync(asset.Url, cancellationToken);
            var temp = target + ".tmp";
            await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
            File.Move(temp, target, true);
        }

        progress?.Report("Face tracking ready");
    }

    private static string FriendlyName(string fileName) => fileName switch
    {
        "haarcascade_frontalface_default.xml" => "face detector",
        "haarcascade_eye_tree_eyeglasses.xml" => "eye detector",
        "haarcascade_smile.xml" => "mouth detector",
        _ => "detector"
    };
}
