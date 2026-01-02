using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Ronin.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Ronin;

/// <summary>
/// Ronin Plugin. Checks your anime on animefillerlist.com and AniDb and updates episode tags.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasPluginConfiguration, IHasWebPages
{

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">The Jellyfin application paths.</param>
    /// <param name="xmlSerializer">The XML serializer used by Jellyfin.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) 
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Ronin";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("c6c0526b-5d62-4cee-9143-833bd43a4e78");

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>
    /// Returns information about the web pages exposed by this plugin for the Jellyfin dashboard.
    /// </summary>
    /// <returns>A collection of <see cref="PluginPageInfo"/> objects.</returns>
    public IEnumerable<PluginPageInfo> GetPages()
    {
        string? prefix = GetType().Namespace;

        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = $"{prefix}.Configuration.configPage.html"
        };
    }
}