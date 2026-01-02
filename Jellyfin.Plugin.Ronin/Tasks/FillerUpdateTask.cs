using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;

using Jellyfin.Plugin.Ronin.Configuration;
using Jellyfin.Plugin.Ronin.Helpers;

namespace Jellyfin.Plugin.Ronin.Tasks;

/// <summary>
/// Scheduled task responsible for updating anime series with filler/canon episode tags
/// based on data from AnimeFillerList.com and AniDB.
/// </summary>
public class FillerUpdateTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<FillerUpdateTask> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PluginConfiguration _config;


    private readonly string[] _fillerTags = ["Manga Canon", "Mixed Canon/Filler", "Filler", "Anime Canon"];

    /// <summary>
    /// Initializes a new instance of the <see cref="FillerUpdateTask"/> class.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager used to query media items.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="httpClientFactory">Factory for creating HTTP clients used for external API calls.</param>
    public FillerUpdateTask(ILibraryManager libraryManager, ILogger<FillerUpdateTask> logger, IHttpClientFactory httpClientFactory)
    {
        _libraryManager = libraryManager;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
    }

    /// <summary>
    /// Gets the rate limit in milliseconds applied between outbound AniDB/AnimeFillerList requests.
    /// </summary>
    private int RequestDelayMs => (_config.DbRateLimitMs > 0) ? _config.DbRateLimitMs : 2000;


    /// <inheritdoc />
    public string Name => "Update Anime Canon/Filler Metadata Tags";
    /// <inheritdoc />
    public string Key => "RoninFillerUpdateTask";
    /// <inheritdoc />
    public string Description => "Checks your anime on animefillerlist.com and updates episode tags to mark canon or filler episodes.";
    /// <inheritdoc />
    public string Category => "Ronin";

    /// <summary>
    /// Gets the default triggers for this scheduled task.  
    /// Executes once every 24 hours.
    /// </summary>
    /// <returns>A collection of default task triggers.</returns>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfoType.IntervalTrigger,
            IntervalTicks = TimeSpan.FromHours(24).Ticks
        };
    }


    /// <summary>
    /// Executes the scheduled filler synchronization task.  
    /// For each anime series, filler data is pulled from AnimeFillerList.com,  
    /// AniDB episodes are resolved, and tags are applied accordingly.
    /// </summary>
    /// <param name="progress">Reports execution progress back to Jellyfin.</param>
    /// <param name="cancellationToken">Token used to cancel execution.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Anime Filler Update Task");

        var seriesList = CollectAnimeSeries.Execute(_libraryManager);

        progress?.Report(0);

        if (seriesList.Count == 0)
        {
            progress?.Report(100);
            return;
        }

        var httpClient = _httpClientFactory.CreateClient("RoninHttpClient");
        double seriesProcessed = 0;

        foreach (var show in seriesList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var slug = show.GetProviderId("TvdbSlug");
            if (string.IsNullOrEmpty(slug))
            {
                slug = GenerateSlug(show.Name);
            }
            var fillerUrl = $"https://www.animefillerlist.com/shows/{slug}";

            Dictionary<int, string> fillerData;
            try
            {
                var response = await httpClient.GetAsync(fillerUrl, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Could not find filler list for {ShowName} at {Url}", show.Name, fillerUrl);
                    seriesProcessed++;
                    progress?.Report(seriesProcessed / seriesList.Count * 100);
                    continue;
                }

                var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                fillerData = ParseHtml(html);
                await Task.Delay(RequestDelayMs, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching filler list for {ShowName}", show.Name);
                seriesProcessed++;
                progress?.Report(seriesProcessed / seriesList.Count * 100);
                continue;
            }

            if (fillerData.Count == 0)
            {
                seriesProcessed++;
                progress?.Report(seriesProcessed / seriesList.Count * 100);
                continue;
            }

            var episodes = _libraryManager.GetItemList(new InternalItemsQuery
            {
                Parent = show,
                IncludeItemTypes = [ BaseItemKind.Episode ],
                Recursive = true,
                IsVirtualItem = false
            })
            .Cast<Episode>()
            .Where(e => e.IndexNumber.HasValue)
            .OrderBy(e => e.ParentIndexNumber ?? 1)
            .ThenBy(e => e.IndexNumber)
            .ToList();

            if (episodes.Count == 0)
            {
                seriesProcessed++;
                progress?.Report(seriesProcessed / seriesList.Count * 100);
                continue;
            }

            double episodeProcessed = 0;

            foreach (var episode in episodes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await ProcessEpisodeAsync(
                    episode,
                    fillerData,
                    httpClient,
                    cancellationToken
                ).ConfigureAwait(false);

                episodeProcessed++;
                progress?.Report((seriesProcessed + (episodeProcessed / episodes.Count)) / seriesList.Count * 100);
            }

            seriesProcessed++;
        }

        progress?.Report(100);
    }

    private Dictionary<int, string> ParseHtml(string html)
    {
        var result = new Dictionary<int, string>();
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var rows = doc.DocumentNode.SelectNodes("//table[@class='EpisodeList']/tbody/tr");
        if (rows == null) return result;

        foreach (var row in rows)
        {
            var numberNode = row.SelectSingleNode(".//td[@class='Number']");
            var typeNode = row.SelectSingleNode(".//td[@class='Type']/span");

            if (numberNode != null && typeNode != null && int.TryParse(numberNode.InnerText.Trim(), out int epNum))
            {
                result[epNum] = typeNode.InnerText.Trim();
            }
        }
        return result;
    }

    private async Task ProcessEpisodeAsync(Episode episode, Dictionary<int, string> fillerData, HttpClient httpClient, CancellationToken cancellationToken)
    {
        // 0. Skip already-processed episodes
        if (episode.Tags != null && episode.Tags.Any(t => _fillerTags.Contains(t)))
        {
            _logger.LogDebug(
                "Episode {EpisodeName} already has filler/canon tag, skipping.",
                episode.Name);
            return;
        }

        int? absoluteNumber = null;

        var series = episode.Series;
        // --- 1. Try TVDB first ---        
        if (series != null)
        {
            absoluteNumber = await ResolveEpisodeNumber.AbsoluteFromTvdbAsync(
                series.GetProviderId("Tvdb"),
                series.GetProviderId("TvdbSlug"),
                episode.GetProviderId("Tvdb"),
                httpClient,
                RequestDelayMs,
                cancellationToken
            ).ConfigureAwait(false);
        }

        // --- 2. Fallback to AniDB ---
        if (!absoluteNumber.HasValue || absoluteNumber < 0)
        {
            absoluteNumber = await ResolveEpisodeNumber.AbsoluteFromAniDbAsync(
                    episode.GetProviderId("AniDB"),
                    httpClient,
                    RequestDelayMs,
                    cancellationToken
            ).ConfigureAwait(false);
        }

        // --- 3. Apply filler tag if resolved ---
        if (!absoluteNumber.HasValue || absoluteNumber < 0)
        {
            _logger.LogDebug("Could not resolve absolute episode number for {EpisodeName}, skipping.", episode.Name);
            return;
        }

        if (!fillerData.TryGetValue(absoluteNumber.Value, out var type)) return;

        episode.Tags = (episode.Tags ?? Array.Empty<string>())
            .Where(t => !_fillerTags.Contains(t))
            .Append(type)
            .Distinct()
            .ToArray();

        await _libraryManager.UpdateItemAsync(
            episode,
            episode,
            ItemUpdateType.MetadataEdit,
            cancellationToken
        ).ConfigureAwait(false);
    }

    private static string GenerateSlug(string title)
    {
        string str = title.ToLowerInvariant();
        str = Regex.Replace(str, @"[^a-z0-9\s-]", "");
        str = Regex.Replace(str, @"\s+", " ").Trim();
        str = str.Substring(0, str.Length <= 45 ? str.Length : 45).Trim();
        str = Regex.Replace(str, @"\s", "-");
        return str;
    }
}