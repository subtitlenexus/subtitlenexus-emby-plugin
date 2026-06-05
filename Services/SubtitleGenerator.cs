using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using SubtitleNexus.Configuration;

namespace SubtitleNexus.Services
{
    
    
    
    
    public enum GenerationOutcome
    {
        
        Skipped,
        
        AlreadyPresent,
        
        Cached,
        
        Generated,
        
        Error,
    }

    
    
    
    
    public class SubtitleGenerator
    {
        private const int PollIntervalS = 20;
        private const int PollMaxWaitS = 60 * 45;   
        private const int InitialPollDelayS = 15;

        private readonly ILogger _logger;
        private readonly ILibraryManager _libraryManager;

        public SubtitleGenerator(ILogger logger, ILibraryManager libraryManager)
        {
            _logger = logger;
            _libraryManager = libraryManager;
        }

        
        
        
        
        public static string SrtOutputPath(string videoPath, string subtitleLanguage)
        {
            var dir = Path.GetDirectoryName(videoPath) ?? string.Empty;
            var stem = Path.GetFileNameWithoutExtension(videoPath);
            return Path.Combine(dir, $"{stem}.{subtitleLanguage}.srt");
        }

        
        
        
        
        
        
        
        
        
        
        public async Task<GenerationOutcome> ProcessItemAsync(BaseItem item, PluginConfiguration cfg,
            bool skipIfSrtExists, IProgress<double> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (item == null || string.IsNullOrEmpty(item.Path))
            {
                _logger?.Warn("[SubtitleNexus] Item has no path; skipping");
                return GenerationOutcome.Skipped;
            }

            string videoPath = item.Path;
            if (!File.Exists(videoPath))
            {
                _logger?.Warn("[SubtitleNexus] Path missing on disk: {0}", videoPath);
                return GenerationOutcome.Skipped;
            }

            string outPath = SrtOutputPath(videoPath, cfg.SubtitleLanguage);
            if (skipIfSrtExists && File.Exists(outPath))
            {
                _logger?.Debug("[SubtitleNexus] SRT already present, skipping: {0}", outPath);
                return GenerationOutcome.AlreadyPresent;
            }

            long durationSeconds = 0;
            if (item.RunTimeTicks.HasValue && item.RunTimeTicks.Value > 0)
            {
                durationSeconds = item.RunTimeTicks.Value / TimeSpan.TicksPerSecond;
            }

            _logger?.Info("[SubtitleNexus] Processing {0} ({1}s)",
                Path.GetFileName(videoPath), durationSeconds);

            string fileHash, fileHashSha256;
            try
            {
                fileHash = NexusHasher.OsHash(videoPath);
                fileHashSha256 = NexusHasher.Sha256Endpoints(videoPath);
            }
            catch (Exception ex)
            {
                _logger?.ErrorException("[SubtitleNexus] Hash failed for " + videoPath, ex);
                return GenerationOutcome.Error;
            }

            var client = new NexusApiClient(cfg.ApiKey, cfg.Domain);
            progress?.Report(0.05);

            
            if (!cfg.DisableSubtitleSearch)
            {
                string scope = cfg.IgnoreCommunitySubs ? "own" : "all";
                try
                {
                    var raw = await client.SearchAsync(fileHash, cfg.Model, cfg.SubtitleLanguage, scope,
                        cancellationToken).ConfigureAwait(false);
                    var res = NexusApiClient.AsObject(raw);
                    if (res.TryGetValue("subtitle_ids", out var ids) && ids is IList<object> idList
                        && idList.Count > 0)
                    {
                        string subtitleId = idList[0]?.ToString();
                        _logger?.Info("[SubtitleNexus] Cache hit: {0}", subtitleId);
                        await DownloadSubtitleAsync(client, subtitleId, outPath,
                            cfg.AutoPurchasePastDailyLimit, cancellationToken).ConfigureAwait(false);
                        progress?.Report(1.0);
                        TriggerLibraryRefresh(item);
                        return GenerationOutcome.Cached;
                    }
                }
                catch (NexusException ex)
                {
                    _logger?.Warn("[SubtitleNexus] Cache search failed ({0}): {1}", ex.Code, ex.Message);
                }
            }

            
            string tmpDir = Path.Combine(Path.GetTempPath(),
                "nexus_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tmpDir);
            string audioPath = Path.Combine(tmpDir,
                Path.GetFileNameWithoutExtension(videoPath) + "." + AudioExtractor.AudioFormat);

            try
            {
                _logger?.Info("[SubtitleNexus] Extracting audio...");
                await AudioExtractor.ExtractAsync(videoPath, audioPath, cfg.FfmpegPath, _logger,
                    cancellationToken).ConfigureAwait(false);
                progress?.Report(0.20);

                long fileSize = new FileInfo(audioPath).Length;
                _logger?.Info("[SubtitleNexus] Uploading audio ({0:F1} MB)...", fileSize / 1048576.0);

                var startRaw = await client.UploadStartAsync(
                    Path.GetFileName(audioPath),
                    AudioExtractor.AudioContentType,
                    fileSize,
                    durationSeconds,
                    cfg.AudioLanguage,
                    cancellationToken).ConfigureAwait(false);
                var start = NexusApiClient.AsObject(startRaw);
                string uploadId = NexusApiClient.GetString(start, "upload_id");
                string presignedUrl = NexusApiClient.GetString(start, "presigned_url");

                await client.UploadToS3Async(presignedUrl, audioPath, AudioExtractor.AudioContentType,
                    cancellationToken).ConfigureAwait(false);
                progress?.Report(0.45);

                await client.UploadFinishAsync(uploadId, cancellationToken).ConfigureAwait(false);

                _logger?.Info("[SubtitleNexus] Submitting subtitle request...");
                var reqRaw = await client.SubmitSubtitleRequestAsync(
                    uploadId, fileHash, fileHashSha256, cfg.AudioLanguage,
                    cfg.SubtitleLanguage, cfg.Model,
                    string.IsNullOrEmpty(cfg.Visibility) ? "PUBLIC" : cfg.Visibility.ToUpperInvariant(),
                    cancellationToken).ConfigureAwait(false);
                var req = NexusApiClient.AsObject(reqRaw);
                string subtitleId = NexusApiClient.GetString(req, "subtitle_id");

                _logger?.Info("[SubtitleNexus] Subtitle request: {0}", subtitleId);
                progress?.Report(0.50);

                
                _logger?.Info("[SubtitleNexus] Waiting for transcription...");
                await PollAndStreamAsync(client, subtitleId, item, outPath,
                    cfg.AutoPurchasePastDailyLimit, progress, cancellationToken).ConfigureAwait(false);

                
                _logger?.Info("[SubtitleNexus] Downloading final SRT...");
                await DownloadSubtitleAsync(client, subtitleId, outPath,
                    cfg.AutoPurchasePastDailyLimit, cancellationToken).ConfigureAwait(false);

                _logger?.Info("[SubtitleNexus] Wrote {0}", Path.GetFileName(outPath));
                progress?.Report(1.0);

                TriggerLibraryRefresh(item);
                return GenerationOutcome.Generated;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
                }
                catch (Exception cleanupEx)
                {
                    _logger?.Warn("[SubtitleNexus] Temp cleanup failed: {0}", cleanupEx.Message);
                }
            }
        }

        
        
        
        
        private async Task DownloadSubtitleAsync(NexusApiClient client, string subtitleId,
            string outPath, bool autoPurchase, CancellationToken ct)
        {
            IDictionary<string, object> linkData;
            try
            {
                linkData = NexusApiClient.AsObject(
                    await client.DownloadLinkAsync(subtitleId, ct: ct).ConfigureAwait(false));
            }
            catch (NexusException ex)
            {
                if (ex.Code == "http_402" && autoPurchase)
                {
                    _logger?.Info("[SubtitleNexus] Daily limit hit, purchasing {0}", subtitleId);
                    await client.PurchaseAsync(subtitleId, ct).ConfigureAwait(false);
                    linkData = NexusApiClient.AsObject(
                        await client.DownloadLinkAsync(subtitleId, ct: ct).ConfigureAwait(false));
                }
                else
                {
                    throw;
                }
            }

            string url = NexusApiClient.GetString(linkData, "download_link");
            if (string.IsNullOrEmpty(url))
            {
                throw new NexusException("No download_link in response");
            }
            await client.DownloadFileAsync(url, outPath, ct: ct).ConfigureAwait(false);
        }

        
        
        
        
        
        private async Task PollAndStreamAsync(NexusApiClient client, string subtitleId,
            BaseItem item, string outPath, bool autoPurchase, IProgress<double> progress,
            CancellationToken ct)
        {
            await Task.Delay(TimeSpan.FromSeconds(InitialPollDelayS), ct).ConfigureAwait(false);
            int elapsed = InitialPollDelayS;
            int lastProgress = -1;
            bool hasFileSeen = false;

            while (elapsed < PollMaxWaitS)
            {
                ct.ThrowIfCancellationRequested();

                IDictionary<string, object> status;
                try
                {
                    status = NexusApiClient.AsObject(
                        await client.PollStatusAsync(subtitleId, ct).ConfigureAwait(false));
                }
                catch (NexusException ex)
                {
                    _logger?.Warn("[SubtitleNexus] Poll error: {0}", ex.Message);
                    await Task.Delay(TimeSpan.FromSeconds(PollIntervalS), ct).ConfigureAwait(false);
                    elapsed += PollIntervalS;
                    continue;
                }

                int prog = (int)NexusApiClient.GetLong(status, "progress");
                if (prog != lastProgress)
                {
                    
                    double overall = 0.50 + (Math.Max(0, Math.Min(100, prog)) / 100.0) * 0.49;
                    progress?.Report(overall);
                    _logger?.Debug("[SubtitleNexus] {0} progress={1}% status={2} has_file={3}",
                        subtitleId, prog,
                        NexusApiClient.GetString(status, "status"),
                        NexusApiClient.GetBool(status, "has_file"));
                    lastProgress = prog;
                }

                var statusStr = NexusApiClient.GetString(status, "status");
                if (statusStr == "FAILED")
                {
                    throw new NexusException(
                        $"Subtitle request FAILED: {NexusApiClient.GetString(status, "error_type")}");
                }

                if (NexusApiClient.GetBool(status, "has_file"))
                {
                    try
                    {
                        await DownloadSubtitleAsync(client, subtitleId, outPath, autoPurchase, ct)
                            .ConfigureAwait(false);
                        long sz = File.Exists(outPath) ? new FileInfo(outPath).Length : 0;
                        _logger?.Info("[SubtitleNexus] Streamed SRT update ({0} bytes, progress={1}%)",
                            sz, prog);
                        if (!hasFileSeen)
                        {
                            hasFileSeen = true;
                            TriggerLibraryRefresh(item);
                        }
                    }
                    catch (NexusException ex)
                    {
                        _logger?.Warn("[SubtitleNexus] Streaming download failed: {0}", ex.Message);
                    }
                }

                if (statusStr == "COMPLETED")
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(PollIntervalS), ct).ConfigureAwait(false);
                elapsed += PollIntervalS;
            }

            throw new NexusException($"Polling exceeded {PollMaxWaitS}s for {subtitleId}");
        }

        
        
        
        
        
        
        
        private void TriggerLibraryRefresh(BaseItem item)
        {
            try
            {
                _libraryManager?.QueueLibraryScan();
                _logger?.Debug("[SubtitleNexus] Queued library scan for caption attach");
            }
            catch (Exception ex)
            {
                _logger?.Warn("[SubtitleNexus] Library scan trigger failed (sidecar is on disk): {0}",
                    ex.Message);
            }
        }

        

        
        
        
        
        public static async Task<IDictionary<string, object>> ValidateAsync(PluginConfiguration cfg,
            CancellationToken ct = default)
        {
            var result = new Dictionary<string, object>();
            if (string.IsNullOrWhiteSpace(cfg.ApiKey))
            {
                result["error"] = "API key not set";
                return result;
            }

            var client = new NexusApiClient(cfg.ApiKey, cfg.Domain);
            try
            {
                await client.HealthAsync(ct).ConfigureAwait(false);
            }
            catch (NexusException ex)
            {
                result["error"] = "Health check failed: " + ex.Message;
                return result;
            }

            try
            {
                var validate = NexusApiClient.AsObject(await client.ValidateKeyAsync(ct).ConfigureAwait(false));
                var user = NexusApiClient.AsObject(await client.UserInfoAsync(ct).ConfigureAwait(false));
                result["valid"] = NexusApiClient.GetBool(validate, "valid");
                result["user"] = NexusApiClient.GetString(user, "username");
                result["plan"] = NexusApiClient.GetString(user, "plan");
                result["subtitle_credits"] = NexusApiClient.GetLong(user, "subtitle_request_credits");
                result["tokens"] = NexusApiClient.GetLong(user, "tokens");
            }
            catch (NexusException ex)
            {
                result["error"] = "Key validation failed: " + ex.Message;
            }
            return result;
        }
    }
}
