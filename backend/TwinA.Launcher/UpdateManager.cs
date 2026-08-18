using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Forms;

namespace TwinA.Launcher;

internal static class UpdateManager
{
    private const string LatestReleaseApi = "https://api.github.com/repos/AhmadAmerBakran/TwinAControlCenter/releases/latest";
    private const string ReleasesPage = "https://github.com/AhmadAmerBakran/TwinAControlCenter/releases/latest";
    private const string ExpectedAssetPrefix = "TwinA-Control-Center-Setup-";
    private const string ExpectedAssetSuffix = "-win-x64.exe";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    internal static string CurrentVersionText
    {
        get
        {
            var version = CurrentVersion;
            return $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    private static Version CurrentVersion
    {
        get
        {
            var raw = typeof(UpdateManager).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);
            return new Version(raw.Major, raw.Minor, Math.Max(raw.Build, 0));
        }
    }

    internal static async Task CheckForUpdatesAsync(
        bool userInitiated,
        Action? beforeInstall = null,
        Action<string>? status = null)
    {
        if (!await Gate.WaitAsync(0)) return;
        var shouldReportErrors = userInitiated;

        try
        {
            using var http = CreateHttpClient();
            using var response = await http.GetAsync(LatestReleaseApi);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                if (userInitiated)
                {
                    MessageBox.Show(
                        "No published TWIN A release could be found on GitHub yet.",
                        "TWIN A — Updates",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                return;
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var release = ParseRelease(json);

            if (release is null)
            {
                if (userInitiated)
                {
                    MessageBox.Show(
                        "TWIN A could not read the latest release information from GitHub.",
                        "TWIN A — Updates",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
                return;
            }

            if (release.Version <= CurrentVersion)
            {
                if (userInitiated)
                {
                    MessageBox.Show(
                        $"TWIN A {CurrentVersionText} is up to date.",
                        "TWIN A — Updates",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                return;
            }

            shouldReportErrors = true;
            var choice = MessageBox.Show(
                $"TWIN A {release.VersionText} is available.\n\n" +
                $"Installed version: {CurrentVersionText}\n" +
                $"Latest version: {release.VersionText}\n\n" +
                "Download and install the verified update now? Windows will ask for administrator permission when the installer starts.",
                "TWIN A — Update available",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (choice != DialogResult.Yes) return;

            status?.Invoke($"Downloading TWIN A {release.VersionText}...");
            var installerPath = await DownloadAndVerifyAsync(http, release);
            status?.Invoke("Update downloaded and verified. Starting installer...");

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /TWINAUPDATE=1",
                WorkingDirectory = Path.GetDirectoryName(installerPath)!,
                UseShellExecute = true,
                Verb = "runas"
            });

            if (process is null)
                throw new InvalidOperationException("Windows did not start the TWIN A update installer.");

            beforeInstall?.Invoke();
            Application.Exit();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            if (shouldReportErrors)
            {
                MessageBox.Show(
                    "The update was cancelled before Windows granted administrator permission. TWIN A was not changed.",
                    "TWIN A — Update cancelled",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            if (shouldReportErrors)
            {
                var openRelease = MessageBox.Show(
                    $"TWIN A could not complete the automatic update.\n\n{ex.Message}\n\nOpen the official Releases page instead?",
                    "TWIN A — Update failed",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (openRelease == DialogResult.Yes) OpenReleasesPage();
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"TWIN-A-Control-Center/{CurrentVersionText}");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2026-03-10");
        return http;
    }

    private static ReleaseInfo? ParseRelease(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("tag_name", out var tagElement)) return null;
        var tag = tagElement.GetString();
        if (string.IsNullOrWhiteSpace(tag) || !TryParseReleaseVersion(tag, out var version)) return null;

        var versionText = $"{version.Major}.{version.Minor}.{version.Build}";
        var expectedAssetName = $"{ExpectedAssetPrefix}{versionText}{ExpectedAssetSuffix}";

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return null;

        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var nameElement)) continue;
            if (!string.Equals(nameElement.GetString(), expectedAssetName, StringComparison.OrdinalIgnoreCase)) continue;

            var downloadUrl = asset.TryGetProperty("browser_download_url", out var urlElement)
                ? urlElement.GetString()
                : null;
            var digest = asset.TryGetProperty("digest", out var digestElement) && digestElement.ValueKind == JsonValueKind.String
                ? digestElement.GetString()
                : null;
            var size = asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var parsedSize)
                ? parsedSize
                : 0;

            if (string.IsNullOrWhiteSpace(downloadUrl) || !IsTrustedReleaseAssetUrl(downloadUrl))
                return null;

            return new ReleaseInfo(version, versionText, new ReleaseAsset(expectedAssetName, downloadUrl, digest, size));
        }

        return null;
    }

    private static bool TryParseReleaseVersion(string tag, out Version version)
    {
        version = new Version(0, 0, 0);
        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];

        var separator = text.IndexOfAny(new[] { '-', '+' });
        if (separator >= 0) text = text[..separator];

        if (!Version.TryParse(text, out var parsed)) return false;
        version = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
        return true;
    }

    private static bool IsTrustedReleaseAssetUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)) return false;

        return uri.AbsolutePath.StartsWith(
            "/AhmadAmerBakran/TwinAControlCenter/releases/download/",
            StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> DownloadAndVerifyAsync(HttpClient http, ReleaseInfo release)
    {
        if (string.IsNullOrWhiteSpace(release.Asset.Digest) ||
            !release.Asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("GitHub did not provide a SHA-256 digest for the release installer, so TWIN A refused to install it automatically.");
        }

        var updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TWIN A",
            "Updates",
            release.VersionText);
        Directory.CreateDirectory(updateDirectory);

        var installerPath = Path.Combine(updateDirectory, release.Asset.Name);
        var partialPath = installerPath + ".download";
        File.Delete(partialPath);

        using (var response = await http.GetAsync(release.Asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var destination = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await source.CopyToAsync(destination);
        }

        var file = new FileInfo(partialPath);
        if (release.Asset.Size > 0 && file.Length != release.Asset.Size)
        {
            File.Delete(partialPath);
            throw new InvalidDataException("The downloaded installer size did not match the GitHub release asset.");
        }

        await using var stream = File.OpenRead(partialPath);
        var hash = await SHA256.HashDataAsync(stream);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        var expected = release.Asset.Digest["sha256:".Length..].Trim().ToLowerInvariant();

        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            File.Delete(partialPath);
            throw new InvalidDataException("The downloaded installer failed SHA-256 verification.");
        }

        File.Move(partialPath, installerPath, overwrite: true);
        return installerPath;
    }

    private static void OpenReleasesPage()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = ReleasesPage, UseShellExecute = true });
        }
        catch { }
    }

    private sealed record ReleaseInfo(Version Version, string VersionText, ReleaseAsset Asset);
    private sealed record ReleaseAsset(string Name, string DownloadUrl, string? Digest, long Size);
}
