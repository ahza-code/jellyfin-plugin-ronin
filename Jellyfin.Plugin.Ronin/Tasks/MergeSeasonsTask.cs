using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using MediaBrowser.Model.Entities;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;

using Jellyfin.Plugin.Ronin.Helpers;
using Jellyfin.Plugin.Ronin.Configuration;

namespace Jellyfin.Plugin.Ronin.Tasks;


/// <summary>
/// Scheduled task that merges all anime seasons into a single season.
/// </summary>
public class MergeAnimeSeasonsTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IDirectoryService _directoryService;
    private readonly ILogger<MergeAnimeSeasonsTask> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PluginConfiguration _config;

    /// <summary>
    /// Initializes a new instance of the <see cref="MergeAnimeSeasonsTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Service for accessing library items.</param>
    /// <param name="directoryService">Service for directory operations.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// /// <param name="httpClientFactory">Factory for creating HTTP clients used for external API calls.</param>
    public MergeAnimeSeasonsTask(ILibraryManager libraryManager, IDirectoryService directoryService, ILogger<MergeAnimeSeasonsTask> logger, IHttpClientFactory httpClientFactory)
    {
        _libraryManager = libraryManager;
        _directoryService = directoryService;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
    }

    private bool RefreshSeriesAfterProcessed => _config.RefreshSeriesAfterProcessed;
    private int RequestDelayMs => (_config.DbRateLimitMs > 0) ? _config.DbRateLimitMs : 2000;
    private bool RenameWhenSingleSeason => _config.RenameWhenSingleSeason;
    private string SingleSeasonName => string.IsNullOrWhiteSpace(_config.SingleSeasonName) ? "Episodes" : _config.SingleSeasonName;

    /// <inheritdoc />
    public string Name => "⚠ Global Anime Re-Org: Force Single Season";
    /// <inheritdoc />
    public string Key => "RoninMergeSeasonsTask";
    /// <inheritdoc />
    public string Description => "Consolidates all episodes into a single season (season 1) for each anime series. This renumbers seasons while keeping episode metadata intact. Specials (Season 0) are untouched. Experimental feature; use with caution.";
    /// <inheritdoc />
    public string Category => "Ronin";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

    /// <summary>
    /// Executes the scheduled season merging task.  
    /// For each anime series, all episodes from multiple seasons are moved into season 1.
    /// </summary>
    /// <param name="progress">Reports execution progress back to Jellyfin.</param>
    /// <param name="cancellationToken">Token used to cancel execution.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Merge Anime Seasons Task");

        var seriesList = CollectAnimeSeries.Execute(_libraryManager);

        progress?.Report(0);

        if (seriesList.Count == 0)
        {
            progress?.Report(100);
            return;
        }

        var httpClient = _httpClientFactory.CreateClient("RoninHttpClient");    
        double seriesProcessed = 0;

        foreach (var series in seriesList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var episodes = _libraryManager.GetItemList(new InternalItemsQuery
            {
                Parent = series,
                IncludeItemTypes = [ BaseItemKind.Episode ],
                Recursive = true,
                IsVirtualItem = false
            })
            .Cast<Episode>()
            .Where(e => e.ParentIndexNumber.HasValue && e.ParentIndexNumber > 0)
            .ToList();

            if (episodes.Count == 0)
            {
                seriesProcessed++;
                progress?.Report(seriesProcessed / seriesList.Count * 100);
                continue;
            }

            bool seriesModified = false;
            double episodeProcessed = 0;

            // Check numbering pattern prior to merging
            var allEpisodeNumbers = episodes
                .Where(e => e.IndexNumber.HasValue && e.IndexNumber > 0)
                .Select(e => e.IndexNumber)
                .OrderBy(n => n)
                .ToList();

            // Condition 1: all numbers sequential (absolute numbers)
            bool isSequentialAbsolute = allEpisodeNumbers.Count > 0
                && allEpisodeNumbers.Distinct().Count() == allEpisodeNumbers.Count
                && allEpisodeNumbers.First() == 1
                && allEpisodeNumbers.Last() == allEpisodeNumbers.Count;

            // Condition 2: likely per-season numbering (episodes repeating 1)
            bool hasDuplicateOnes = allEpisodeNumbers.GroupBy(n => n).Any(g => g.Count() > 1 && g.Key == 1);

            foreach (var episode in episodes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (episode.ParentIndexNumber == 1)
                {
                    episodeProcessed++;
                    progress?.Report((seriesProcessed + (episodeProcessed / episodes.Count)) / seriesList.Count * 100);
                    continue;
                }

                _logger.LogInformation(
                    "Merging {Series} - {Episode}: Season {Old} → 1",
                    series?.Name,
                    episode.Name,
                    episode.ParentIndexNumber
                );

                episode.ParentIndexNumber = 1;
                // episode.ParentId = Guid.Empty;

                // --- Recalculate Episode Number if needed ---
                if (!isSequentialAbsolute || hasDuplicateOnes)
                {

                    int? absoluteNumber = null;

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

                    if (absoluteNumber.HasValue && absoluteNumber > 0)
                    {
                        episode.IndexNumber = absoluteNumber;
                    }

                }

                await _libraryManager.UpdateItemAsync(
                    episode,
                    episode,
                    ItemUpdateType.MetadataEdit,
                    cancellationToken
                ).ConfigureAwait(false);

                seriesModified = true;

                episodeProcessed++;
                progress?.Report((seriesProcessed + (episodeProcessed / episodes.Count)) / seriesList.Count * 100);
            }

            // Refresh the Series Metadata to update the "Season 1" counts in the UI
            if (RefreshSeriesAfterProcessed && seriesModified)
            {
                try 
                {
                    _logger.LogInformation("Refreshing metadata for series: {Series}", series?.Name);
                    
                    var refreshOptions = new MetadataRefreshOptions(_directoryService)
                    {
                        MetadataRefreshMode = MetadataRefreshMode.Default,
                        ImageRefreshMode = MetadataRefreshMode.Default,
                        ReplaceAllMetadata = false,
                        ReplaceAllImages = false,
                        ForceSave = true
                    };
                    if (series != null)
                    {
                        await series.ValidateChildren(new Progress<double>(), CancellationToken.None).ConfigureAwait(false);
                        await series.RefreshMetadata(refreshOptions, CancellationToken.None).ConfigureAwait(false);
                    }

                    // Find all seasons for this series
                    var seasons = _libraryManager.GetItemList(new InternalItemsQuery
                    {
                        Parent = series,
                        IncludeItemTypes = [BaseItemKind.Season]
                    }).Cast<Season>().ToList();

                    foreach (var season in seasons)
                    {
                        // === Rename Season 1 if user requested ===
                        if (season.IndexNumber == 1 && RenameWhenSingleSeason)
                        {
                            if (!string.Equals(season.Name, SingleSeasonName, StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogInformation("Renaming season: {Old} → {New}", season.Name, SingleSeasonName);
                                season.Name = SingleSeasonName;

                                await _libraryManager.UpdateItemAsync(
                                    season,
                                    season,
                                    ItemUpdateType.MetadataEdit,
                                    cancellationToken
                                ).ConfigureAwait(false);
                            }
                        }
                        // Delete empty seasons that aren't Season 1 (or 0)
                        if (season.IndexNumber > 1)
                        {
                            _logger.LogInformation("Removing empty season: {Series} - Season {Number}", series?.Name, season.IndexNumber);
                            // Directly delete. We know it's empty because we just moved all episodes to Season 1.
                            _libraryManager.DeleteItem(season, new DeleteOptions { DeleteFileLocation = false });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to refresh metadata for series {Series}", series?.Name);
                }
            }

            seriesProcessed++;
        }

        progress?.Report(100);
        _logger.LogInformation("Finished merging all anime seasons into season 1.");
    }
}