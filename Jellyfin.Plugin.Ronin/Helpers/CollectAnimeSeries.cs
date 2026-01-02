using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using Jellyfin.Data.Enums;

using Jellyfin.Plugin.Ronin.Configuration;


namespace Jellyfin.Plugin.Ronin.Helpers;

/// <summary>
/// Collects anime series from the Jellyfin library based on the plugin configuration.
/// </summary>
public static class CollectAnimeSeries
{
    /// <summary>
    /// Executes the collection of anime series from the library.
    /// </summary>
    /// <param name="libraryManager">The Jellyfin library manager used to query media items.</param>
    /// <returns>An immutable list of series identified as anime.</returns>
    public static IReadOnlyList<Series> Execute(ILibraryManager libraryManager)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();

        var allSeries = libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [ BaseItemKind.Series ],
            Recursive = true
        })
        .Cast<Series>();

        return allSeries
            .Where(s => IsAnime(s, config))
            .ToList()
            .AsReadOnly();
    }

    private static bool IsAnime(Series series, PluginConfiguration config)
    {
        var hasGenre = series.Genres?.Any(
            g => string.Equals(g, "Anime", StringComparison.OrdinalIgnoreCase)) == true;

        var targetTag = string.IsNullOrWhiteSpace(config.AnimeTargetTag) ? "Anime" : config.AnimeTargetTag;

        var hasTag = series.Tags?.Any(
            t => string.Equals(t, targetTag ?? "Anime", StringComparison.OrdinalIgnoreCase)) == true;


        return config.AnimeIdentificationMode switch
        {
            AnimeIdentificationMode.Genre => hasGenre,
            AnimeIdentificationMode.Tag => hasTag,
            AnimeIdentificationMode.GenreOrTag => hasGenre || hasTag,
            AnimeIdentificationMode.GenreAndTag => hasGenre && hasTag,
            _ => hasGenre
        };
    }
}