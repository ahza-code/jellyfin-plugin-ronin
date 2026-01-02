using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Ronin.Tasks;
using Jellyfin.Plugin.Ronin.Helpers;

/// <summary>
/// Scheduled task that removes all filler/canon tags previously assigned by the Ronin plugin.
/// </summary>
public class FillerResetTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<FillerResetTask> _logger;

    private readonly string[] _fillerTags = ["Manga Canon", "Mixed Canon/Filler", "Filler", "Anime Canon"];

    /// <summary>
    /// Initializes a new instance of the <see cref="FillerResetTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Service for accessing and updating library items.</param>
    /// <param name="logger">Logging instance for diagnostic output.</param>
    public FillerResetTask(ILibraryManager libraryManager, ILogger<FillerResetTask> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Reset Anime Canon/Filler Metadata Tags";
    /// <inheritdoc />
    public string Key => "RoninFillerResetTask";
    /// <inheritdoc />
    public string Description => "Removes all filler/canon tags previously set by the Ronin Plugin.";
    /// <inheritdoc />
    public string Category => "Ronin";

    /// <summary>
    /// Gets the default triggers for this scheduled task.
    /// </summary>
    /// <returns>An empty sequence, indicating that this task is only run manually.</returns>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return Array.Empty<TaskTriggerInfo>();
    }

    /// <summary>
    /// Executes the task, removing filler tags from all anime series and episodes in the library.
    /// </summary>
    /// <param name="progress">Progress reporter for task execution.</param>
    /// <param name="cancellationToken">Token used to signal task cancellation.</param>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Anime Filler Tags Reset Task");

        var seriesList = CollectAnimeSeries.Execute(_libraryManager);

        double current = 0;
        double total = seriesList.Count;

        foreach (var show in seriesList)
        {
            current++;
            progress?.Report((current / total) * 100);

            var episodes = _libraryManager.GetItemList(new InternalItemsQuery
            {
                Parent = show,
                IncludeItemTypes = [ BaseItemKind.Episode ],
                Recursive = true,
                IsVirtualItem = false
            })
            .Cast<Episode>()
            .Where(e => e.IndexNumber.HasValue)
            .ToList();

            foreach (var episode in episodes)
            {
                if (episode.Tags == null || episode.Tags.Length == 0)
                    continue;

                var updated = episode.Tags.Where(t => !_fillerTags.Contains(t)).ToArray();

                if (!updated.SequenceEqual(episode.Tags))
                {
                    episode.Tags = updated;

                    await _libraryManager.UpdateItemAsync(
                        episode,
                        episode,
                        ItemUpdateType.MetadataEdit,
                        cancellationToken
                    ).ConfigureAwait(false);

                    _logger.LogInformation("Reset filler tags for episode: {EpisodeName}", episode.Name);
                }
            }
        }

        progress?.Report(100);
        _logger.LogInformation("Finished resetting anime filler tags.");
    }
}