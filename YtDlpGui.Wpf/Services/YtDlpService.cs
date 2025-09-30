using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using YtDlpGui.Wpf.Models;

namespace YtDlpGui.Wpf.Services
{
    public class YtDlpService
    {
        public string YtDlpPath { get; set; } = "yt-dlp.exe"; // если в PATH

        public async Task<YtDlpInfo> GetInfoAsync(string url, CancellationToken ct)
        {
            var args = $"-J --no-warnings --ignore-config --no-playlist \"{url}\"";
            var json = await RunAndReadStdOutAsync(YtDlpPath, args, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("yt-dlp вернул пустой JSON. Проверьте ссылку или доступность yt-dlp.");

            // 1) JavaScriptSerializer (встроенный в .NET Framework)
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };
            var obj = serializer.Deserialize<YtDlpInfo>(json);
            return obj;

            // 2) Альтернатива: System.Text.Json (требует пакет в .NET Framework)
            //return DeserializeWithSystemTextJson(json);
        }

        public async Task<int> DownloadAsync(string url, string formatSelector, string outputTemplate, string subLangs, bool writeSubs,
                                            IProgress<(double percent, string line)> progress, CancellationToken ct)
        {
            var sb = new StringBuilder();
            sb.Append($"-f {formatSelector} ");
            sb.Append("--ignore-config ");
            if (writeSubs && !string.IsNullOrWhiteSpace(subLangs))
            {
                sb.Append($"--write-subs --sub-langs \"{subLangs}\" ");
            }
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
                var tcs = new TaskCompletionSource<int>();
                p.Exited += (_, __) => tcs.TrySetResult(p.ExitCode);

                p.Start();
                string stdout = await p.StandardOutput.ReadToEndAsync();
                string stderr = await p.StandardError.ReadToEndAsync();

                await tcs.Task;

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
                p.ErrorDataReceived += (_, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    var (percent, line) = Utils.ProgressLineParser.TryParse(e.Data);
                    progress?.Report((percent, e.Data));
                };

                p.Start();
                p.BeginErrorReadLine();
                // stdout часто не нужен, но вычитаем чтобы не заблокироваться
                _ = p.StandardOutput.ReadToEndAsync();

                await Task.Run(() => p.WaitForExit(), ct);
                return p.ExitCode;
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
