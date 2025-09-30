using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using YtDlpGui.Wpf.Models;

namespace YtDlpGui.Wpf.Services
{
    public class YtDlpService
    {
        public string YtDlpPath { get; set; } = "yt-dlp.exe";

        public async Task<YtDlpInfo> GetInfoAsync(string url, CancellationToken ct)
        {
            var args = $"-J --no-warnings --ignore-config --no-playlist \"{url}\"";
            var json = await RunAndReadStdOutAsync(YtDlpPath, args, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("yt-dlp вернул пустой JSON. Проверьте ссылку или доступность yt-dlp.");

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };
            var obj = serializer.Deserialize<YtDlpInfo>(json);
            return obj;

            // Альтернатива: System.Text.Json (нужен пакет в .NET Framework 4.7.2)
            //return System.Text.Json.JsonSerializer.Deserialize<YtDlpInfo>(json,
            //    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        public async Task<int> DownloadAsync(string url, string formatSelector, string outputTemplate, string subLangs, bool writeSubs,
                                    IProgress<(double percent, string line)> progress, CancellationToken ct)
{
    var sb = new StringBuilder();
    sb.Append($"-f {formatSelector} ");
    sb.Append("--ignore-config ");
    // важное: сделать строки «построчными» и без ANSI
    sb.Append("--newline --no-color ");
    if (writeSubs && !string.IsNullOrWhiteSpace(subLangs))
        sb.Append($"--write-subs --sub-langs \"{subLangs}\" ");
    sb.Append($"-o \"{outputTemplate}\" ");
    sb.Append($"\"{url}\"");

    return await RunWithProgressAsync(YtDlpPath, sb.ToString(), progress, ct).ConfigureAwait(false);
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

        p.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            var (percent, line) = Utils.ProgressLineParser.TryParse(e.Data);
            progress?.Report((percent, e.Data));
        };

        p.Exited += (_, __) =>
        {
            try { tcsExit.TrySetResult(p.ExitCode); } catch { }
        };

        p.Start();
        p.BeginErrorReadLine();
        // читаем stdout, чтобы не заблокироваться
        var stdoutTask = p.StandardOutput.ReadToEndAsync();

        // связываем отмену с убийством процесса
        using (ct.Register(() =>
        {
            try
            {
                if (!p.HasExited)
                    p.Kill();
            }
            catch { }
            finally
            {
                tcsExit.TrySetCanceled();
            }
        }))
        {
            try
            {
                var exitCode = await tcsExit.Task.ConfigureAwait(false);
                // дочитаем stdout
                try { await stdoutTask.ConfigureAwait(false); } catch { }
                return exitCode;
            }
            catch (TaskCanceledException)
            {
                throw new OperationCanceledException(ct);
            }
        }
    }
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

        
    }
}
