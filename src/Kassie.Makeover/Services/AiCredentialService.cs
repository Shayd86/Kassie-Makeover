using System.IO;
using System.Security.Cryptography;
using System.Text;
using Kassie.Makeover.Infrastructure;

namespace Kassie.Makeover.Services;

public sealed class AiCredentialService
{
    private readonly string _keyPath = Path.Combine(AppPaths.Config, "openai-api-key.dpapi");

    public string? GetOpenAiApiKey()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment.Trim();

        try
        {
            if (!File.Exists(_keyPath))
                return null;

            var encrypted = File.ReadAllBytes(_keyPath);
            var bytes = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var key = Encoding.UTF8.GetString(bytes).Trim();
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }
        catch (Exception ex)
        {
            AppLog.Write($"Could not read protected OpenAI API key: {ex.Message}");
            return null;
        }
    }

    public bool IsUsingEnvironmentKey()
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY"));

    public void SaveOpenAiApiKey(string apiKey)
    {
        apiKey = apiKey.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("Enter an API key first.", nameof(apiKey));

        AppPaths.Ensure();
        var bytes = Encoding.UTF8.GetBytes(apiKey);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_keyPath, encrypted);
    }

    public void ForgetOpenAiApiKey()
    {
        if (File.Exists(_keyPath))
            File.Delete(_keyPath);
    }
}
