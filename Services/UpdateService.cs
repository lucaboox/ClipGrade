using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace ClipboardApp.Services;

public sealed record UpdateCheckResult(
    bool IsUpdateAvailable,
    string CurrentVersion,
    string? LatestVersion,
    string? ReleaseName,
    string? ReleaseUrl,
    string? DownloadUrl,
    string? ErrorMessage);

public static class UpdateService
{
    private const string Owner = "lucaboox";
    private const string Repo = "ClipGrade";
    private const string LatestReleaseUrl = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

    public static string CurrentVersion
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version == null
                ? "Unknown"
                : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public static async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ClipGrade-update-checker");
            client.Timeout = TimeSpan.FromSeconds(12);

            using var response = await client.GetAsync(LatestReleaseUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Failed($"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = json.RootElement;

            string? tag = ReadString(root, "tag_name");
            string? releaseName = ReadString(root, "name");
            string? releaseUrl = ReadString(root, "html_url");
            string? downloadUrl = FindBestDownloadUrl(root);

            if (string.IsNullOrWhiteSpace(tag))
                return Failed("The latest GitHub release did not include a version tag.");

            bool available = IsNewerVersion(tag, CurrentVersion);
            return new UpdateCheckResult(
                available,
                CurrentVersion,
                CleanVersion(tag),
                releaseName,
                releaseUrl,
                downloadUrl,
                null);
        }
        catch (OperationCanceledException)
        {
            return Failed("The update check was cancelled.");
        }
        catch (Exception ex)
        {
            return Failed(ex.Message);
        }

        static UpdateCheckResult Failed(string message) =>
            new(false, CurrentVersion, null, null, null, null, message);
    }

    public static void OpenUpdatePage(UpdateCheckResult result)
    {
        var url = result.DownloadUrl ?? result.ReleaseUrl ?? $"https://github.com/{Owner}/{Repo}/releases";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static string? FindBestDownloadUrl(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return ReadString(root, "html_url");

        string? firstAsset = null;
        foreach (var asset in assets.EnumerateArray())
        {
            string? name = ReadString(asset, "name");
            string? url = ReadString(asset, "browser_download_url");
            if (string.IsNullOrWhiteSpace(url)) continue;

            firstAsset ??= url;
            if (name?.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == true)
                return url;
        }

        return firstAsset ?? ReadString(root, "html_url");
    }

    private static bool IsNewerVersion(string latest, string current)
    {
        if (!Version.TryParse(CleanVersion(latest), out var latestVersion))
            return !string.Equals(latest.Trim(), current.Trim(), StringComparison.OrdinalIgnoreCase);

        if (!Version.TryParse(CleanVersion(current), out var currentVersion))
            return false;

        return latestVersion > currentVersion;
    }

    private static string CleanVersion(string version)
    {
        version = version.Trim().TrimStart('v', 'V');
        int suffix = version.IndexOfAny(['-', '+']);
        return suffix >= 0 ? version[..suffix] : version;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
