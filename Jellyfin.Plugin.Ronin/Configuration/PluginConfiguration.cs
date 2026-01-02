using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Ronin.Configuration;

/// <summary>
/// Plugin configuration for Ronin.
/// Contains settings used by the plugin tasks and frontend injection.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets whether canon/filler badges should appear on individual episode pages.
    /// </summary>
    public bool ShowBadgesOnEpisodePage { get; set; } = true;

    /// <summary>
    /// Gets or sets whether canon/filler badges should appear in season episode lists.
    /// </summary>
    public bool ShowBadgesOnSeasonList { get; set; } = true;

    /// <summary>
    /// Gets or sets whether badges should use colored styling (e.g., red filler, green canon).
    /// </summary>
    public bool EnableBadgeColors { get; set; } = true;

    /// <summary>
    /// Gets or sets minimum delay between external DB (TheTVDB/AniDB) API requests in milliseconds to comply with rate limits and avoid temporary blocks.
    /// </summary>
    public int DbRateLimitMs { get; set; } = 2000;

    /// <summary>
    /// Determines how anime series are identified (Genre, Tag, or combination).
    /// </summary>
    public AnimeIdentificationMode AnimeIdentificationMode { get; set; }
        = AnimeIdentificationMode.Genre;

    /// <summary>
    /// Tag name used when AnimeIdentificationMode includes Tag.
    /// </summary>
    public string AnimeTargetTag { get; set; } = "Anime";

    /// <summary>
    /// Gets or sets a value indicating whether to trigger a non-destructive metadata refresh for each series after its episodes have been re-indexed. 
    /// This updates the UI episode counts and season structure immediately.
    /// </summary>
    public bool RefreshSeriesAfterProcessed { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to rename the season folder when all episodes are merged into a single season.
    /// </summary>
    public bool RenameWhenSingleSeason { get; set; } = true;

    /// <summary>
    /// Gets or sets the name to assign to the single season when merging all episodes into one season.
    /// </summary>
    public string SingleSeasonName { get; set; } = "Episodes";
}

/// <summary>
/// Modes for identifying anime series in the library.
/// </summary>
public enum AnimeIdentificationMode
{
    /// <summary>
    /// Identify anime series by Genre only.
    /// </summary>
    Genre,
    /// <summary>
    /// Identify anime series by Tag only.
    /// </summary>
    Tag,
    /// <summary>
    /// Identify anime series by Genre or Tag.
    /// </summary>
    GenreOrTag,
    /// <summary>
    /// Identify anime series by both Genre and Tag.
    /// </summary>
    GenreAndTag
}