using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;
using SubtitleNexus.Services;

namespace SubtitleNexus.ScheduledTasks
{
    
    
    
    
    
    
    
    
    
    public class ScanLibraryTask : IScheduledTask, IConfigurableScheduledTask
    {
        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;

        public ScanLibraryTask(ILogger logger, ILibraryManager libraryManager)
        {
            _logger = logger;
            _libraryManager = libraryManager;
        }

        public string Name => "Generate missing Subtitle Nexus subtitles";
        public string Key  => "SubtitleNexusScanLibrary";
        public string Category => "Subtitle Nexus";
        public string Description =>
            "Scan the library for videos that are missing a {filename}.{lang}.srt sidecar in " +
            "the configured language and generate one via subtitlenexus.com.";

        public bool IsEnabled => true;
        public bool IsHidden => false;
        public bool IsLogged => true;

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerWeekly,
                DayOfWeek = DayOfWeek.Sunday,
                TimeOfDayTicks = TimeSpan.FromHours(3).Ticks,
            }
        };

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                _logger?.Error("[SubtitleNexus] Plugin instance is null; aborting task");
                return;
            }
            var cfg = plugin.PluginConfiguration;
            if (string.IsNullOrWhiteSpace(cfg.ApiKey))
            {
                _logger?.Warn("[SubtitleNexus] API key not configured; aborting task");
                return;
            }

            
            
            
            
            var query = new InternalItemsQuery
            {
                Recursive = true,
                IncludeItemTypes = new[] { "Movie", "Episode", "Video" },
                IsVirtualItem = false,
            };
            BaseItem[] items;
            try
            {
                items = _libraryManager.GetItemList(query);
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("[SubtitleNexus] Library query failed", ex);
                return;
            }

            var pending = new List<BaseItem>();
            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item?.Path)) continue;
                if (!File.Exists(item.Path)) continue;
                var srt = SubtitleGenerator.SrtOutputPath(item.Path, cfg.SubtitleLanguage);
                if (!File.Exists(srt)) pending.Add(item);
            }

            _logger?.Info("[SubtitleNexus] Scan found {0} items missing {1}.srt", pending.Count,
                cfg.SubtitleLanguage);
            if (pending.Count == 0)
            {
                progress?.Report(100);
                return;
            }

            int parallelism = Math.Max(1, Math.Min(cfg.MaxConcurrentJobs, 8));
            int processed = 0;
            int total = pending.Count;
            var generator = new SubtitleGenerator(_logger, _libraryManager);
            using (var sem = new SemaphoreSlim(parallelism, parallelism))
            {
                var tasks = new List<Task>();
                foreach (var item in pending)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await sem.WaitAsync(cancellationToken).ConfigureAwait(false);

                    BaseItem captured = item;
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var outcome = await generator.ProcessItemAsync(captured, cfg,
                                skipIfSrtExists: true, progress: null,
                                cancellationToken: cancellationToken).ConfigureAwait(false);
                            _logger?.Info("[SubtitleNexus] {0}: {1}",
                                Path.GetFileName(captured.Path), outcome);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger?.ErrorException(
                                "[SubtitleNexus] Job failed for " + captured.Path, ex);
                        }
                        finally
                        {
                            int done = Interlocked.Increment(ref processed);
                            progress?.Report(100.0 * done / total);
                            sem.Release();
                        }
                    }, cancellationToken));
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            _logger?.Info("[SubtitleNexus] Scan complete: {0}/{1} processed", processed, total);
            progress?.Report(100);
        }
    }
}
