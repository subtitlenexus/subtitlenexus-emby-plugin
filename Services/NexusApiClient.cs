using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SubtitleNexus.Services
{
    
    
    
    public class NexusException : Exception
    {
        public int? StatusCode { get; }
        public string Code { get; }

        public NexusException(string message, int? statusCode = null, string code = null)
            : base(message)
        {
            StatusCode = statusCode;
            Code = code;
        }

        public NexusException(string message, Exception inner, int? statusCode = null, string code = null)
            : base(message, inner)
        {
            StatusCode = statusCode;
            Code = code;
        }
    }

    
    
    
    
    
    
    
    
    
    
    public class NexusApiClient
    {
        public const string ApplicationType = "emby";
        public const int PipelineVersion = 2;
        public const int DefaultTimeoutSeconds = 30;

        private readonly string _apiKey;
        private readonly string _domain;
        private readonly HttpClient _http;

        public NexusApiClient(string apiKey, string domain = "api.subtitlenexus.com",
                              int timeoutSeconds = DefaultTimeoutSeconds)
        {
            _apiKey = apiKey;
            _domain = string.IsNullOrWhiteSpace(domain) ? "api.subtitlenexus.com" : domain;

            
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
                
            }

            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
            _http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private string Url(string path) => $"https://{_domain}{path}";

        private async Task<object> RequestAsync(HttpMethod method, string path,
            string jsonBody = null, bool skipAuth = false,
            CancellationToken cancellationToken = default)
        {
            using (var req = new HttpRequestMessage(method, Url(path)))
            {
                if (!skipAuth)
                {
                    req.Headers.TryAddWithoutValidation("X-API-Key", _apiKey);
                }
                if (jsonBody != null)
                {
                    req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                }

                HttpResponseMessage resp;
                try
                {
                    resp = await _http.SendAsync(req, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    throw new NexusException($"{method} {path} transport error: {ex.Message}", ex);
                }

                using (resp)
                {
                    string body = resp.Content == null
                        ? string.Empty
                        : await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!resp.IsSuccessStatusCode)
                    {
                        int code = (int)resp.StatusCode;
                        throw new NexusException(
                            $"{method} {path} -> {code}: {Truncate(body, 500)}",
                            statusCode: code,
                            code: $"http_{code}");
                    }

                    if (resp.StatusCode == HttpStatusCode.NoContent || string.IsNullOrEmpty(body))
                    {
                        return null;
                    }

                    return JsonReader.Parse(body);
                }
            }
        }

        private static string Truncate(string s, int n) =>
            s == null ? string.Empty : (s.Length <= n ? s : s.Substring(0, n));

        

        
        public static IDictionary<string, object> AsObject(object node) =>
            node as IDictionary<string, object> ?? new Dictionary<string, object>();

        public static string GetString(IDictionary<string, object> obj, string key)
        {
            if (obj == null) return null;
            if (!obj.TryGetValue(key, out var v) || v == null) return null;
            return v.ToString();
        }

        public static long GetLong(IDictionary<string, object> obj, string key, long defaultValue = 0)
        {
            if (obj == null) return defaultValue;
            if (!obj.TryGetValue(key, out var v) || v == null) return defaultValue;
            if (v is long l) return l;
            if (v is int i) return i;
            if (v is double d) return (long)d;
            if (long.TryParse(v.ToString(), out var parsed)) return parsed;
            return defaultValue;
        }

        public static bool GetBool(IDictionary<string, object> obj, string key)
        {
            if (obj == null) return false;
            if (!obj.TryGetValue(key, out var v) || v == null) return false;
            if (v is bool b) return b;
            return bool.TryParse(v.ToString(), out var parsed) && parsed;
        }

        

        public Task<object> HealthAsync(CancellationToken ct = default)
            => RequestAsync(HttpMethod.Get, "/v1/health/", skipAuth: true, cancellationToken: ct);

        public Task<object> ValidateKeyAsync(CancellationToken ct = default)
            => RequestAsync(HttpMethod.Get, "/v1/user/validate/", cancellationToken: ct);

        public Task<object> UserInfoAsync(CancellationToken ct = default)
            => RequestAsync(HttpMethod.Get, "/v1/user/info/", cancellationToken: ct);

        public async Task<IList<object>> VersionsAsync(CancellationToken ct = default)
        {
            var res = await RequestAsync(HttpMethod.Get, "/v1/ai/versions/", cancellationToken: ct)
                .ConfigureAwait(false);
            return res as IList<object> ?? new List<object>();
        }

        

        public Task<object> CostAsync(string version, string subtitleLanguage,
            long? durationSeconds = null, CancellationToken ct = default)
        {
            var qs = $"?version={Uri.EscapeDataString(version)}" +
                     $"&subtitle_language={Uri.EscapeDataString(subtitleLanguage)}";
            if (durationSeconds.HasValue)
            {
                qs += $"&duration_seconds={durationSeconds.Value}";
            }
            return RequestAsync(HttpMethod.Get, "/v1/ai/subtitle-request/cost/" + qs, cancellationToken: ct);
        }

        public Task<object> SearchAsync(string fileHash, string version, string language,
            string scope = "all", CancellationToken ct = default)
        {
            var path = $"/v1/subtitle/search/?file_hash={Uri.EscapeDataString(fileHash)}" +
                       $"&version={Uri.EscapeDataString(version)}" +
                       $"&language={Uri.EscapeDataString(language)}" +
                       $"&scope={Uri.EscapeDataString(scope)}";
            return RequestAsync(HttpMethod.Get, path, cancellationToken: ct);
        }

        

        public Task<object> UploadStartAsync(string fileName, string contentType, long fileSize,
            long durationSeconds, string audioLanguage, CancellationToken ct = default)
        {
            var body = JsonWriter.Object(new Dictionary<string, object>
            {
                ["file_name"] = fileName,
                ["content_type"] = contentType,
                ["file_size"] = fileSize,
                ["duration_seconds"] = durationSeconds,
                ["upload_type"] = "simple",
                ["audio_language"] = audioLanguage,
                ["application_type"] = ApplicationType,
            });
            return RequestAsync(HttpMethod.Post, "/v1/async-upload/av/start/", body, cancellationToken: ct);
        }

        public async Task UploadToS3Async(string presignedUrl, string filePath, string contentType,
            CancellationToken ct = default)
        {
            
            using (var s3Client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var content = new StreamContent(fs))
            {
                content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                content.Headers.ContentLength = fs.Length;

                using (var resp = await s3Client.PutAsync(presignedUrl, content, ct).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        string body = resp.Content == null
                            ? string.Empty
                            : await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        int code = (int)resp.StatusCode;
                        throw new NexusException(
                            $"S3 PUT failed: {code} {Truncate(body, 200)}",
                            statusCode: code,
                            code: $"s3_{code}");
                    }
                }
            }
        }

        public Task<object> UploadFinishAsync(string uploadId, CancellationToken ct = default)
        {
            var body = JsonWriter.Object(new Dictionary<string, object>
            {
                ["upload_id"] = uploadId,
                ["upload_type"] = "simple",
            });
            return RequestAsync(HttpMethod.Post, "/v1/async-upload/av/finish/", body, cancellationToken: ct);
        }

        

        public Task<object> SubmitSubtitleRequestAsync(string uploadId, string fileHash,
            string fileHashSha256, string audioLanguage, string subtitleLanguage, string version,
            string visibility = "PUBLIC", CancellationToken ct = default)
        {
            var body = JsonWriter.Object(new Dictionary<string, object>
            {
                ["upload_id"] = uploadId,
                ["file_hash"] = fileHash,
                ["file_hash_sha256"] = fileHashSha256,
                ["audio_language"] = audioLanguage,
                ["subtitle_language"] = subtitleLanguage,
                ["version"] = version,
                ["pipeline_version"] = PipelineVersion,
                ["application_type"] = ApplicationType,
                ["visibility"] = visibility,
                ["auto_route"] = true,
            });
            return RequestAsync(HttpMethod.Post, "/v1/ai/subtitle-request/", body, cancellationToken: ct);
        }

        public Task<object> PollStatusAsync(string subtitleId, CancellationToken ct = default)
            => RequestAsync(HttpMethod.Get,
                $"/v1/ai/subtitle-request/?subtitle_id={Uri.EscapeDataString(subtitleId)}",
                cancellationToken: ct);

        public Task<object> DownloadLinkAsync(string subtitleId, int expirationS = 3600,
            CancellationToken ct = default)
            => RequestAsync(HttpMethod.Get,
                $"/v1/subtitle/download/?subtitle_id={Uri.EscapeDataString(subtitleId)}" +
                $"&expiration_s={expirationS}",
                cancellationToken: ct);

        public Task<object> PurchaseAsync(string subtitleId, CancellationToken ct = default)
        {
            var body = JsonWriter.Object(new Dictionary<string, object>
            {
                ["subtitle_id"] = subtitleId,
            });
            return RequestAsync(HttpMethod.Post, "/v1/subtitle/purchase/", body, cancellationToken: ct);
        }

        

        
        
        
        
        public async Task DownloadFileAsync(string url, string destPath, int timeoutSeconds = 300,
            CancellationToken ct = default)
        {
            using (var dl = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) })
            using (var resp = await dl.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false))
            {
                if (!resp.IsSuccessStatusCode)
                {
                    int code = (int)resp.StatusCode;
                    throw new NexusException(
                        $"Download failed: {code}",
                        statusCode: code,
                        code: $"http_{code}");
                }

                using (var input = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = new FileStream(destPath, FileMode.Create, FileAccess.Write,
                    FileShare.None, 1 << 16, useAsync: true))
                {
                    var buffer = new byte[1 << 16];
                    int n;
                    while ((n = await input.ReadAsync(buffer, 0, buffer.Length, ct)
                        .ConfigureAwait(false)) > 0)
                    {
                        await output.WriteAsync(buffer, 0, n, ct).ConfigureAwait(false);
                    }
                }
            }
        }
    }
}
