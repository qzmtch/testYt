using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using YtDlpGui.Wpf.Models;
using YtDlpGui.Wpf.Utils;

namespace YtDlpGui.Wpf.Services
{
    public class YtDlpService
    {
        public string YtDlpPath { get; set; } = "yt-dlp.exe"; // если в PATH

        public async Task<YtDlpInfo> GetInfoAsync(string url, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("URL пуст.", nameof(url));

            // --no-color и UTF-8, чтобы исключить управляющие последовательности
            var args = $"-J --no-warnings --ignore-config --no-playlist --no-color \"{url}\"";
            var json = await RunAndReadStdOutAsync(YtDlpPath, args, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("yt-dlp вернул пустой JSON. Проверьте ссылку или доступность yt-dlp.");

            // 1) JavaScriptSerializer (встроенный в .NET Framework)
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 512 };
            try
            {
                var obj = serializer.Deserialize<YtDlpInfo>(json);
                return obj;
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException("Не удалось распарсить JSON, полученный от yt-dlp.", ex);
            }

            // 2) Альтернатива: System.Text.Json (нужен пакет для .NET Framework)
            // return DeserializeWithSystemTextJson(json);
        }

        public async Task<int> DownloadAsync(
            string url,
            string formatSelector,
            string outputTemplate,
            string subLangs,
            bool writeSubs,
            IProgress<(double percent, string line)> progress,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("URL пуст.", nameof(url));
            if (string.IsNullOrWhiteSpace(formatSelector))
                formatSelector = "best";
            if (string.IsNullOrWhiteSpace(outputTemplate))
                outputTemplate = Path.Combine(Environment.CurrentDirectory, "%(title)s [%(id)s].%(ext)s");

            var sb = new StringBuilder();
            sb.Append($"-f {formatSelector} ");
            sb.Append("--ignore-config ");
            // важные флаги для стабильного прогресса построчно и без ANSI
            sb.Append("--newline --no-color ");
            if (writeSubs && !string.IsNullOrWhiteSpace(subLangs))
                sb.Append($"--write-subs --sub-langs \"{subLangs}\" ");
            sb.Append($"-o \"{outputTemplate}\" ");
            sb.Append($"\"{url}\"");

            return await RunWithProgressAsync(YtDlpPath, sb.ToString(), progress, ct).ConfigureAwait(false);
        }

        private async Task<string> RunAndReadStdOutAsync(string file, string args, CancellationToken ct)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using (var p = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                var tcsExit = new TaskCompletionSource<int>();

                p.Exited += (_, __) =>
                {
                    try { tcsExit.TrySetResult(p.ExitCode); } catch { }
                };

                p.Start();

                var stdoutTask = p.StandardOutput.ReadToEndAsync();
                var stderrTask = p.StandardError.ReadToEndAsync();

                using (ct.Register(() =>
                {
                    try { if (!p.HasExited) p.Kill(); } catch { }
                    tcsExit.TrySetCanceled();
                }))
                {
                    try
                    {
                        await tcsExit.Task.ConfigureAwait(false);
                    }
                    catch (TaskCanceledException)
                    {
                        throw new OperationCanceledException(ct);
                    }
                }

                string stdout = string.Empty;
                string stderr = string.Empty;
                try { stdout = await stdoutTask.ConfigureAwait(false); } catch { }
                try { stderr = await stderrTask.ConfigureAwait(false); } catch { }

                if (p.ExitCode != 0 && string.IsNullOrWhiteSpace(stdout))
                    throw new InvalidOperationException($"yt-dlp завершился с ошибкой {p.ExitCode}. STDERR: {stderr}");

                return stdout;
            }
        }

        private async Task<int> RunWithProgressAsync(string file, string args, IProgress<(double, string)> progress, CancellationToken ct)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using (var p = new Process { StartInfo = psi, EnableRaisingEvents = true })
            {
                var tcsExit = new TaskCompletionSource<int>();

                DataReceivedEventHandler onData = (s, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    var (percent, line) = ProgressLineParser.TryParse(e.Data);
                    progress?.Report((percent, e.Data));
                };

                p.OutputDataReceived += onData; // прогресс может идти и в STDOUT с --newline
                p.ErrorDataReceived += onData;  // и в STDERR по умолчанию

                p.Exited += (_, __) =>
                {
                    try { tcsExit.TrySetResult(p.ExitCode); } catch { }
                };

                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                using (ct.Register(() =>
                {
                    try { if (!p.HasExited) p.Kill(); } catch { }
                    tcsExit.TrySetCanceled();
                }))
                {
                    try
                    {
                        var code = await tcsExit.Task.ConfigureAwait(false);
                        return code;
                    }
                    catch (TaskCanceledException)
                    {
                        throw new OperationCanceledException(ct);
                    }
                    finally
                    {
                        try { p.CancelOutputRead(); } catch { }
                        try { p.CancelErrorRead(); } catch { }
                    }
                }
            }
        }

#if USE_SYSTEM_TEXT_JSON
        // Требуется NuGet пакет System.Text.Json для .NET Framework
        private YtDlpInfo DeserializeWithSystemTextJson(string json)
        {
            var options = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
            };
            return System.Text.Json.JsonSerializer.Deserialize<YtDlpInfo>(json, options);
        }
#endif
    }
}
