using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Ronin.Configuration;

namespace Jellyfin.Plugin.Ronin.Helpers;

/// <summary>
/// Provides methods to inject custom scripts and styles into Jellyfin's web frontend via FileTransformation.
/// </summary>
public static class TransformationPatch
{
    /// <summary>
    /// Injects the Ronin frontend CSS and JS into the provided HTML content.
    /// </summary>
    /// <param name="payload">The payload object provided by the FileTransformation plugin. Expects a "contents" property containing HTML.</param>
    /// <returns>The modified HTML content with injected CSS and JS, or the original content if injection fails.</returns>
    public static string InjectIntoIndexHtml(PatchRequestPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.Contents))
            return payload.Contents ?? string.Empty;

        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();

        // If all badge displays are disabled, do NOT inject anything.
        if (!config.ShowBadgesOnEpisodePage && !config.ShowBadgesOnSeasonList)
            return payload.Contents;

        // Hardcoded configuration for frontend usage
        string configScript = $@"
<script>
window.RoninVariables = {{
    RONIN_SHOW_EPISODE_BADGES: {config.ShowBadgesOnEpisodePage.ToString().ToLowerInvariant()},
    RONIN_SHOW_SEASON_LIST_BADGES: {config.ShowBadgesOnSeasonList.ToString().ToLowerInvariant()},
    RONIN_ENABLE_BADGE_COLORS: {config.EnableBadgeColors.ToString().ToLowerInvariant()}
}};
</script>";

        string importedJS = ReadEmbeddedResource($"{typeof(Plugin).Namespace}.Inject.ronin.js");
        string importedCSS = ReadEmbeddedResource($"{typeof(Plugin).Namespace}.Inject.ronin.css");

        // Inject Config JS and CSS before </head>
        string result = Regex.Replace(payload.Contents, "(</head>)", $"{configScript}<style>{importedCSS}</style>$1", RegexOptions.IgnoreCase);
        // Inject Config and JS before </body>
        result = Regex.Replace(result, "(</body>)", $"{configScript}<script defer>{importedJS}</script>$1", RegexOptions.IgnoreCase);

        return result;
    }

    private static string ReadEmbeddedResource(string resourceName)
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream == null) return string.Empty;

        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// Payload passed by FileTransformation containing the HTML content.
/// </summary>
public class PatchRequestPayload
{
    /// <summary>
    /// Gets or sets the HTML content provided by the FileTransformation plugin.
    /// This is the string that will be modified to inject CSS and JavaScript for Ronin.
    /// </summary>
    [JsonPropertyName("contents")]
    public string? Contents { get; set; }
}