using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SupremeCommanderEditor.Core.Services;

/// <summary>Result of a GitHub-release lookup.</summary>
public record UpdateInfo(bool IsUpdateAvailable, string CurrentVersion, string LatestVersion, string ReleaseUrl);

/// <summary>
/// Non-intrusive update notifier: pings GitHub's latest-release endpoint and compares the tag
/// against the running assembly's version. We don't auto-download or auto-install — a banner in
/// the editor surfaces the result and the user opens the release page in a browser when they want.
///
/// Network failures, rate-limits, or pre-release tags are swallowed silently and reported as
/// "no update available". This service must never block startup or surface error popups.
/// </summary>
public static class UpdateCheckService
{
    // matthieucarme/supcom-map-editor — change here if the repo ever moves.
    private const string LatestReleaseApi = "https://api.github.com/repos/matthieucarme/supcom-map-editor/releases/latest";

    // A single shared client — HttpClient is meant to be reused, not constructed per call.
    // Lazy<T> guards against the (rare) case where this is called from multiple threads at once.
    private static readonly Lazy<HttpClient> _client = new(() =>
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        // GitHub rejects requests with no User-Agent. Anything stable works.
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SupremeCommanderMapEditor", "1.0"));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    });

    /// <summary>Fetch the latest release tag from GitHub and compare to <paramref name="currentVersion"/>.
    /// Always returns a value — network errors collapse to <c>IsUpdateAvailable=false</c>.</summary>
    public static async Task<UpdateInfo> CheckAsync(string currentVersion, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _client.Value.GetAsync(LatestReleaseApi, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return new UpdateInfo(false, currentVersion, "", "");

            var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;
            string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            string url = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(tag))
                return new UpdateInfo(false, currentVersion, "", "");

            // Skip pre-releases — they're for testers, not "you should upgrade" prompts.
            if (root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True)
                return new UpdateInfo(false, currentVersion, tag, url);

            bool newer = IsNewer(tag, currentVersion);
            return new UpdateInfo(newer, currentVersion, tag, url);
        }
        catch
        {
            // No network, DNS failure, rate-limit, JSON parse error… all collapse to "nothing to do".
            return new UpdateInfo(false, currentVersion, "", "");
        }
    }

    /// <summary>True iff <paramref name="latestTag"/> is strictly greater than <paramref name="currentVersion"/>.
    /// Both forms accept an optional leading 'v' (e.g. "v1.2.0" vs "1.2.0").</summary>
    public static bool IsNewer(string latestTag, string currentVersion)
    {
        if (!TryParseVersion(latestTag, out var latest) || !TryParseVersion(currentVersion, out var current))
            return false;
        return latest > current;
    }

    private static bool TryParseVersion(string raw, out Version v)
    {
        v = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var s = raw.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        // Drop any trailing pre-release/build suffix ("1.2.0-rc1" → "1.2.0") so System.Version parses.
        int cut = s.IndexOfAny(['-', '+']);
        if (cut > 0) s = s[..cut];
        return Version.TryParse(s, out v!);
    }
}
