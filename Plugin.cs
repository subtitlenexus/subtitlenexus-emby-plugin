using System;
using System.Collections.Generic;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using SubtitleNexus.Configuration;

namespace SubtitleNexus
{
    
    
    
    
    
    
    
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public static Plugin Instance { get; private set; }

        public override string Name => "Subtitle Nexus";

        public override string Description =>
            "Generate AI subtitles via Subtitle Nexus (subtitlenexus.com). " +
            "Hashes each video, looks up cached subtitles, otherwise extracts audio with " +
            "ffmpeg and submits a transcription request. Writes SRT sidecars next to the video.";

        
        private readonly Guid _id = new Guid("4b8a2c5f-7e3d-4f9a-8c1b-9d6e2a3b4c5d");
        public override Guid Id => _id;

        
        
        
        public PluginConfiguration PluginConfiguration => Configuration;

        public IEnumerable<PluginPageInfo> GetPages() => new[]
        {
            new PluginPageInfo
            {
                Name = "subtitlenexus",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
            }
        };
    }
}
