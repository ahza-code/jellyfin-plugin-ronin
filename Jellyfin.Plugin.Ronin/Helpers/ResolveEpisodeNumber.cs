using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace Jellyfin.Plugin.Ronin.Helpers;

/// <summary>
/// Provides utilities to resolve absolute episode numbers
/// from AniDB and TheTVDB.
/// </summary>
public static class ResolveEpisodeNumber
{
    /// <summary>
    /// Resolves an absolute episode number from AniDB given an episode ID.
    /// </summary>
    /// <returns>The absolute episode number or null if not found.</returns>
    public static async Task<int?> AbsoluteFromAniDbAsync(string? anidbEpisodeId, HttpClient client, int RequestDelayMs, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(anidbEpisodeId)) return null;
        var url = $"https://anidb.net/episode/{anidbEpisodeId}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko)");

        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var match = Regex.Match(html, @"- (\d+) -");

        await Task.Delay(RequestDelayMs, cancellationToken).ConfigureAwait(false);

        if (!match.Success) return null;

        return int.TryParse(
            match.Groups[1].Value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var absolute)
            ? absolute
            : null;
    }

    /// <summary>
    /// Resolves an absolute episode number from TheTVDB.
    /// Requires the Tvdb series ID or slug and the episode ID.
    /// </summary>
    /// <returns>The absolute episode number or null if not resolvable.</returns>
    public static async Task<int?> AbsoluteFromTvdbAsync(string? tvdbSeriesId, string? tvdbSeriesSlug, string? tvdbEpisodeId, HttpClient client, int RequestDelayMs, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(tvdbEpisodeId)) return null;
        string? url = tvdbSeriesId is not null
            ? $"https://www.thetvdb.com/series/{tvdbSeriesId}/episodes/{tvdbEpisodeId}"
            : tvdbSeriesSlug is not null
                ? $"https://www.thetvdb.com/series/{tvdbSeriesSlug}/episodes/{tvdbEpisodeId}"
                : null;

        if (url is null) return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        await Task.Delay(RequestDelayMs, cancellationToken).ConfigureAwait(false);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var absoluteCrumb = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'crumbs')][.//a[contains(@href,'/seasons/absolute/')]]");

        if (absoluteCrumb is null) return null;

        var match = Regex.Match(absoluteCrumb.InnerText, @"Episode\s+(\d+)", RegexOptions.IgnoreCase);

        if (!match.Success) return null;

        return int.TryParse(
                match.Groups[1].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var absolute)
            ? absolute
            : null;
    }


    /// <summary>
    /// Resolves a relative aired episode number from TheTVDB.
    /// Requires the Tvdb series ID or slug and the episode ID.
    /// </summary>
    /// <returns>The relative aired episode number or null if not resolvable.</returns>
    public static async Task<int?> RelativeAiredFromTvdbAsync(string? tvdbSeriesId, string? tvdbSeriesSlug, string? tvdbEpisodeId, HttpClient client, int RequestDelayMs, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(tvdbEpisodeId)) return null;
        string? url = tvdbSeriesId is not null
            ? $"https://www.thetvdb.com/series/{tvdbSeriesId}/episodes/{tvdbEpisodeId}"
            : tvdbSeriesSlug is not null
                ? $"https://www.thetvdb.com/series/{tvdbSeriesSlug}/episodes/{tvdbEpisodeId}"
                : null;

        if (url is null) return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        await Task.Delay(RequestDelayMs, cancellationToken).ConfigureAwait(false);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var absoluteCrumb = doc.DocumentNode.SelectSingleNode("//div[contains(@class,'crumbs')][.//a[contains(@href,'/seasons/official/')]]");

        if (absoluteCrumb is null) return null;

        var match = Regex.Match(absoluteCrumb.InnerText, @"Episode\s+(\d+)", RegexOptions.IgnoreCase);

        if (!match.Success) return null;

        return int.TryParse(
                match.Groups[1].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var absolute)
            ? absolute
            : null;
    }
}