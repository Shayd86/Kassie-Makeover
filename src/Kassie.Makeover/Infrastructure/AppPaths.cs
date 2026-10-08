using System.IO;

namespace Kassie.Makeover.Infrastructure;

public static class AppPaths
{
    public const string Root = @"D:\Kassie\Makeover";
    public static string Models => Path.Combine(Root, "models");
    public static string Runtimes => Path.Combine(Root, "runtimes");
    public static string Cache => Path.Combine(Root, "cache");
    public static string Temp => Path.Combine(Root, "temp");
    public static string Outputs => Path.Combine(Root, "outputs");
    public static string Exports => Path.Combine(Root, "exports");
    public static string Wardrobe => Path.Combine(Root, "wardrobe");
    public static string Hair => Path.Combine(Root, "hair");
    public static string HairReferences => Path.Combine(Hair, "references");
    public static string Config => Path.Combine(Root, "config");
    public static string Logs => Path.Combine(Root, "logs");

    public static void Ensure()
    {
        foreach (var path in new[] { Root, Models, Runtimes, Cache, Temp, Outputs, Exports, Wardrobe, Hair, HairReferences, Config, Logs })
            Directory.CreateDirectory(path);
    }
}

public static class AppLog
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Logs);
                File.AppendAllText(Path.Combine(AppPaths.Logs, "app.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }

    public static void Camera(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Logs);
                File.AppendAllText(Path.Combine(AppPaths.Logs, "camera.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
