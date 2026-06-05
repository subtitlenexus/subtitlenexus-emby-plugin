using MediaBrowser.Model.Plugins;

namespace SubtitleNexus.Configuration
{
    
    
    
    
    
    
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string ApiKey { get; set; } = string.Empty;

        public string Domain { get; set; } = "api.subtitlenexus.com";

        
        public string Model { get; set; } = "lulu-2605";

        
        public string SubtitleLanguage { get; set; } = "en";

        
        public string AudioLanguage { get; set; } = "ja";

        
        public string Visibility { get; set; } = "PUBLIC";

        
        public bool IgnoreCommunitySubs { get; set; }

        
        public bool DisableSubtitleSearch { get; set; }

        
        public bool AutoPurchasePastDailyLimit { get; set; }

        
        public string FfmpegPath { get; set; } = string.Empty;

        
        
        
        
        public int MaxConcurrentJobs { get; set; } = 1;
    }
}
