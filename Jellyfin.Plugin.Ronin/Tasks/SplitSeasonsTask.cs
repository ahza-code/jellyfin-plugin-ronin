using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;

using Jellyfin.Plugin.Ronin.Helpers;
using Jellyfin.Plugin.Ronin.Configuration;

namespace Jellyfin.Plugin.Ronin.Tasks;

/// <summary>
/// Scheduled task that normalizes anime episode season numbers using TheTVDB Aired Order.
/// </summary>
public class SplitSeasonsTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IDirectoryService _directoryService;
    private readonly ILogger<SplitSeasonsTask> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PluginConfiguration _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="SplitSeasonsTask"/> class.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager used to query media items.</param>
    /// <param name="directoryService">Service for directory operations.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="httpClientFactory">Factory for creating HTTP clients used for external API calls.</param>
    public SplitSeasonsTask(
        ILibraryManager libraryManager,
        IDirectoryService directoryService,
        ILogger<SplitSeasonsTask> logger,
        IHttpClientFactory httpClientFactory)
    {
        _libraryManager = libraryManager;
        _directoryService = directoryService;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
    }

    /// <summary>
    /// Gets the rate limit in milliseconds applied between outbound AniDB/AnimeFillerList requests.
    /// </summary>
    private int RequestDelayMs => (_config.DbRateLimitMs > 0) ? _config.DbRateLimitMs : 2000;
    /// <summary>
    /// Gets the user preference for automatically refreshing series metadata to update the interface after changes are applied.
    /// </summary>
    private bool RefreshSeriesAfterProcessed => _config.RefreshSeriesAfterProcessed;

    /// <inheritdoc />
    public string Name => "⚠ Global Anime Re-Org: Organize in Aired Seasons";
    /// <inheritdoc />
    public string Key => "RoninSplitSeasonsTask";
    /// <inheritdoc />
    public string Description => "Redistributes episodes into seasons based on TVDB Aired Order while preserving episode numbers. Specials (Season 0) are not affected. Experimental feature; use with caution.";
    /// <inheritdoc />
    public string Category => "Ronin";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

    /// <summary>
    /// Executes the scheduled season splitting task.  
    /// For each anime series, each episode is queried against TheTVDB to determine its aired season number.
    /// </summary>
    /// <param name="progress">Reports execution progress back to Jellyfin.</param>
    /// <param name="cancellationToken">Token used to cancel execution.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting TVDB anime season splitting task");

        var seriesList = CollectAnimeSeries.Execute(_libraryManager);

        progress?.Report(0);

        var client = _httpClientFactory.CreateClient("RoninHttpClient");
        double seriesProcessed = 0;

        foreach (var series in seriesList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tvdbId = series.GetProviderId("Tvdb");
            var tvdbSlug = series.GetProviderId("TvdbSlug");

            if (string.IsNullOrEmpty(tvdbId) && string.IsNullOrEmpty(tvdbSlug))
            {
                seriesProcessed++;
                progress?.Report(seriesProcessed / seriesList.Count * 100);
                continue;
            }

            var episodes = _libraryManager.GetItemList(new InternalItemsQuery
            {
                Parent = series,
                IncludeItemTypes = [ BaseItemKind.Episode ],
                Recursive = true,
                IsVirtualItem = false
            })
            .Cast<Episode>()
            .Where(e => e.GetProviderId("Tvdb") != null)
            .ToList();

            if (episodes.Count == 0)
            {
                seriesProcessed++;
                progress?.Report(seriesProcessed / seriesList.Count * 100);
                continue;
            }

            bool seriesModified = false;
            double episodeProcessed = 0;

            foreach (var episode in episodes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var seasonAiredNumber = await ResolveSeasonNumber.AiredFromTvdbAsync(
                    tvdbId,
                    tvdbSlug,
                    episode.GetProviderId("Tvdb"),
                    client,
                    RequestDelayMs,
                    cancellationToken
                ).ConfigureAwait(false);

                if (seasonAiredNumber > 1 && episode.ParentIndexNumber != seasonAiredNumber)
                {
                    _logger.LogInformation(
                        "Updating {Series} - {Episode}: Season {Old} → {New}",
                        series.Name,
                        episode.Name,
                        episode.ParentIndexNumber ?? 1,
                        seasonAiredNumber);

                    episode.ParentIndexNumber = seasonAiredNumber;
                    // episode.ParentId = Guid.Empty;

                    await _libraryManager.UpdateItemAsync(
                        episode,
                        episode,
                        ItemUpdateType.MetadataEdit,
                        cancellationToken
                    ).ConfigureAwait(false);

                    seriesModified = true;
                }

                episodeProcessed++;
                progress?.Report((seriesProcessed + (episodeProcessed / episodes.Count)) / seriesList.Count * 100);
            }

            // Refresh the Series Metadata to update the "Season 1" counts in the UI
            if (RefreshSeriesAfterProcessed && seriesModified)
            {
                try 
                {
                    _logger.LogInformation("Refreshing metadata for series: {Series}", series.Name);
                    
                    var refreshOptions = new MetadataRefreshOptions(_directoryService)
                    {
                        MetadataRefreshMode = MetadataRefreshMode.Default,
                        ImageRefreshMode = MetadataRefreshMode.Default,
                        ReplaceAllMetadata = false,
                        ReplaceAllImages = false,
                        ForceSave = true
                    };

                    await series.ValidateChildren(new Progress<double>(), CancellationToken.None).ConfigureAwait(false);
                    await series.RefreshMetadata(refreshOptions, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to refresh metadata for series {Series}", series.Name);
                }
            }

            seriesProcessed++;
        }

        progress?.Report(100);
    }
}