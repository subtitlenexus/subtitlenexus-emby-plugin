using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;

namespace SubtitleNexus.Services
{
    
    
    
    
    
    public static class AudioExtractor
    {
        public const string AudioFormat = "mp3";
        public const string AudioContentType = "audio/mpeg";

        public static async Task ExtractAsync(string videoPath, string outPath, string ffmpegPath,
            ILogger logger, CancellationToken cancellationToken = default)
        {
            string exe = string.IsNullOrWhiteSpace(ffmpegPath) ? "ffmpeg" : ffmpegPath;

            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList_Add("-y");
            psi.ArgumentList_Add("-loglevel");
            psi.ArgumentList_Add("error");
            psi.ArgumentList_Add("-i");
            psi.ArgumentList_Add(videoPath);
            psi.ArgumentList_Add("-vn");
            psi.ArgumentList_Add("-sn");
            psi.ArgumentList_Add("-ac");
            psi.ArgumentList_Add("1");
            psi.ArgumentList_Add("-ar");
            psi.ArgumentList_Add("16000");
            psi.ArgumentList_Add("-codec:a");
            psi.ArgumentList_Add("libmp3lame");
            psi.ArgumentList_Add("-b:a");
            psi.ArgumentList_Add("64k");
            psi.ArgumentList_Add(outPath);

            logger?.Info("[SubtitleNexus] ffmpeg {0} {1}", exe, psi.ArgumentsForLog());

            using (var proc = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                var stderr = new StringBuilder();
                proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
                proc.OutputDataReceived += (_, __) => {  };

                if (!proc.Start())
                {
                    throw new InvalidOperationException(
                        $"Failed to start ffmpeg ({exe}). Set FfmpegPath in plugin settings.");
                }

                proc.BeginErrorReadLine();
                proc.BeginOutputReadLine();

                using (cancellationToken.Register(() =>
                {
                    try { if (!proc.HasExited) proc.Kill(); } catch { }
                }))
                {
                    await Task.Run(() => proc.WaitForExit(), cancellationToken).ConfigureAwait(false);
                }

                if (proc.ExitCode != 0 || !File.Exists(outPath))
                {
                    var msg = stderr.ToString();
                    if (msg.Length > 500) msg = msg.Substring(0, 500);
                    throw new InvalidOperationException(
                        $"ffmpeg failed (exit {proc.ExitCode}): {msg.Trim()}");
                }
            }
        }
    }

    
    
    
    
    
    internal static class ProcessStartInfoExtensions
    {
        public static void ArgumentList_Add(this ProcessStartInfo psi, string arg)
        {
            if (psi.Arguments.Length > 0) psi.Arguments += " ";
            psi.Arguments += Quote(arg);
        }

        public static string ArgumentsForLog(this ProcessStartInfo psi) => psi.Arguments;

        
        
        
        
        
        private static string Quote(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            bool needs = false;
            foreach (var c in s)
            {
                if (char.IsWhiteSpace(c) || c == '"') { needs = true; break; }
            }
            if (!needs) return s;
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
