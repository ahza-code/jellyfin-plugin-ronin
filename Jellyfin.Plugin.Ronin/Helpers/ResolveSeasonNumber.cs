using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;

using Jellyfin.Plugin.Ronin.Configuration;

namespace Jellyfin.Plugin.Ronin.Helpers;

/// <summary>
/// Provides utilities to resolve relative season number of an episode from TheTVDB.
/// </summary>
public static class ResolveSeasonNumber
{
    /// <summary>
    /// Resolves a relative aired season number from TheTVDB.
    /// Requires the Tvdb series ID or slug and the episode ID.
    /// </summary>
    /// <returns>The relative aired season number or 1 if not resolvable.</returns>
    public static async Task<int> AiredFromTvdbAsync(string? tvdbSeriesId, string? tvdbSeriesSlug, string? tvdbEpisodeId, HttpClient client, int RequestDelayMs, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(tvdbEpisodeId)) return 1;
        string? url = tvdbSeriesId is not null
            ? $"https://www.thetvdb.com/series/{tvdbSeriesId}/episodes/{tvdbEpisodeId}"
            : tvdbSeriesSlug is not null
                ? $"https://www.thetvdb.com/series/{tvdbSeriesSlug}/episodes/{tvdbEpisodeId}"
                : null;

        if (url is null) return 1;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

        var response = await client.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return 1;

        var html = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        await Task.Delay(RequestDelayMs, cancellationToken).ConfigureAwait(false);

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var seasonNode = doc.DocumentNode.SelectSingleNode("//a[contains(@href,'/seasons/official/')]");

        if (seasonNode == null) return 1;

        var match = Regex.Match(seasonNode.InnerText, @"Season\s+(\d+)");

        if (!match.Success) return 1;

        return int.TryParse(
                match.Groups[1].Value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var season)
            ? season
            : 1;
    }
}