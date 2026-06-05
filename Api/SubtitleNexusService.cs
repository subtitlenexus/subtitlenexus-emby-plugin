using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Services;
using SubtitleNexus.Services;

namespace SubtitleNexus.Api
{
    
    
    
    
    
    
    
    
    
    [Route("/SubtitleNexus/Generate/{ItemId}", "POST",
        Summary = "Generate Nexus subtitles for one item")]
    [Authenticated(Roles = "Admin")]
    public class GenerateForItem : IReturn<object>
    {
        public long ItemId { get; set; }
    }

    
    
    
    
    [Route("/SubtitleNexus/Validate", "GET",
        Summary = "Validate the configured Subtitle Nexus API key")]
    [Authenticated(Roles = "Admin")]
    public class ValidateKey : IReturn<object>
    {
    }

    
    
    
    
    
    public class SubtitleNexusService : IService
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;

        public SubtitleNexusService(ILogger logger, ILibraryManager libraryManager)
        {
            _logger = logger;
            _libraryManager = libraryManager;
        }

        public async Task<object> Post(GenerateForItem request)
        {
            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                return new Dictionary<string, object> { ["error"] = "Plugin not loaded" };
            }
            var cfg = plugin.PluginConfiguration;
            if (string.IsNullOrWhiteSpace(cfg.ApiKey))
            {
                return new Dictionary<string, object>
                {
                    ["error"] = "API key not set. Configure it in the plugin settings.",
                };
            }

            var item = _libraryManager.GetItemById(request.ItemId);
            if (item == null)
            {
                return new Dictionary<string, object>
                {
                    ["error"] = $"Item {request.ItemId} not found",
                };
            }

            var generator = new SubtitleGenerator(_logger, _libraryManager);
            try
            {
                
                var outcome = await generator.ProcessItemAsync(item, cfg,
                    skipIfSrtExists: false, progress: null,
                    cancellationToken: CancellationToken.None).ConfigureAwait(false);

                return new Dictionary<string, object>
                {
                    ["item_id"] = request.ItemId,
                    ["name"] = item.Name,
                    ["result"] = outcome.ToString(),
                };
            }
            catch (NexusException ex)
            {
                _logger?.ErrorException("[SubtitleNexus] API error", ex);
                return new Dictionary<string, object>
                {
                    ["error"] = $"Nexus error: {ex.Message}",
                    ["code"] = ex.Code,
                };
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("[SubtitleNexus] Unhandled error", ex);
                return new Dictionary<string, object>
                {
                    ["error"] = $"Failed: {ex.Message}",
                };
            }
        }

        public async Task<object> Get(ValidateKey request)
        {
            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                return new Dictionary<string, object> { ["error"] = "Plugin not loaded" };
            }
            return await SubtitleGenerator.ValidateAsync(plugin.PluginConfiguration)
                .ConfigureAwait(false);
        }
    }
}
