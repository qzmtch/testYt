using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
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

            var args = $"-J --no-warnings --ignore-config --no-playlist --no-color --newline \"{url}\"";
            var json = await RunAndReadStdOutAsync(YtDlpPath, args, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidOperationException("yt-dlp вернул пустой JSON. Проверьте ссылку или доступность yt-dlp.");

            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 512 };
            try
            {
                return serializer.Deserialize<YtDlpInfo>(json);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException("Не удалось распарсить JSON, полученный от yt-dlp.", ex);
            }
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
            sb.Append("--newline --no-color "); // важные флаги для читаемого прогресса
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
                RedirectStandardInput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            using (var p = new Process { StartInfo = psi, EnableRaisingEvents = true })
            using (var job = new ProcessJob()) // на всякий случай, чтобы корректно гасить дочерние
            {
                var tcsExit = new TaskCompletionSource<int>();

                p.Exited += (_, __) =>
                {
                    try { tcsExit.TrySetResult(p.ExitCode); } catch { }
                };

                p.Start();
                try { job.Assign(p); } catch { /* если не получилось — не критично */ }

                var stdoutTask = p.StandardOutput.ReadToEndAsync();
                var stderrTask = p.StandardError.ReadToEndAsync();

                using (ct.Register(() =>
                {
                    // мягкая попытка
                    try { if (!p.HasExited) { p.StandardInput.WriteLine("q"); p.StandardInput.Flush(); } } catch { }
                    // жёсткая остановка всего дерева через Job
                    try { if (!p.HasExited) job.Terminate(1); } catch { }
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
                RedirectStandardInput = true, // чтобы можно было послать 'q'
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            string lastDestPath = null;
            var rxDest = new Regex(@"```math
download```\s+Destination:\s+(?<p>.+)$", RegexOptions.Compiled);

            bool cancelRequested = false;

            using (var p = new Process { StartInfo = psi, EnableRaisingEvents = true })
            using (var job = new ProcessJob()) // чтобы гарантированно убить дочерние процессы
            {
                var tcsExit = new TaskCompletionSource<int>();

                DataReceivedEventHandler onData = (s, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;

                    // прогресс
                    var (percent, line) = ProgressLineParser.TryParse(e.Data);
                    progress?.Report((percent, e.Data));

                    // путь итогового файла
                    var m = rxDest.Match(e.Data);
                    if (m.Success)
                    {
                        var path = m.Groups["p"].Value.Trim();
                        if (path.Length > 1 && ((path[0] == '"' && path[path.Length - 1] == '"') ||
                                                (path[0] == '“' && path[path.Length - 1] == '”')))
                        {
                            path = path.Substring(1, path.Length - 2);
                        }
                        lastDestPath = path;
                    }
                };

                p.OutputDataReceived += onData;
                p.ErrorDataReceived += onData;

                p.Exited += (_, __) =>
                {
                    try { tcsExit.TrySetResult(p.ExitCode); } catch { }
                };

                p.Start();
                try { job.Assign(p); } catch { }
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                using (ct.Register(() =>
                {
                    cancelRequested = true;

                    // 1) мягкая попытка остановки
                    try { if (!p.HasExited) { p.StandardInput.WriteLine("q"); p.StandardInput.Flush(); } } catch { }

                    // 2) немного подождать (даём шанс самому завершиться)
                    Task.Delay(500).ContinueWith(_ =>
                    {
                        try
                        {
                            if (!p.HasExited)
                                job.Terminate(1); // жёстко гасим всё дерево
                        }
                        catch { }
                    });
                }))
                {
                    int code = 0;
                    try
                    {
                        code = await tcsExit.Task.ConfigureAwait(false);
                    }
                    finally
                    {
                        try { p.CancelOutputRead(); } catch { }
                        try { p.CancelErrorRead(); } catch { }
                    }

                    // если отменяли — чистим недокачанные файлы и сообщаем отмену
                    if (cancelRequested)
                    {
                        await CleanupPartialAsync(lastDestPath).ConfigureAwait(false);
                        throw new OperationCanceledException(ct);
                    }

                    return code;
                }
            }
        }

        private static async Task CleanupPartialAsync(string destPath)
        {
            if (string.IsNullOrWhiteSpace(destPath)) return;

            // Обычно yt-dlp пишет: "Destination: C:\...file.ext"
            // При незавершённой загрузке остаются: file.ext.part, file.ext.ytdl и сам file.ext (иногда нулевого размера)
            var candidates = new[]
            {
                destPath + ".part",
                destPath + ".ytdl",
                destPath
            };

            foreach (var p in candidates)
            {
                try
                {
                    if (File.Exists(p))
                    {
                        // несколько попыток, вдруг дескриптор ещё не отпущен
                        for (int i = 0; i < 8; i++)
                        {
                            try
                            {
                                File.Delete(p);
                                break;
                            }
                            catch
                            {
                                await Task.Delay(200).ConfigureAwait(false);
                            }
                        }
                    }
                }
                catch { }
            }
        }

        // ----------------- Job Object utils (убивает всё дерево процессов) -----------------

        private sealed class ProcessJob : IDisposable
        {
            private IntPtr _hJob = IntPtr.Zero;
            private bool _killOnCloseSet;

            public ProcessJob()
            {
                _hJob = CreateJobObject(IntPtr.Zero, null);
                if (_hJob == IntPtr.Zero) return;

                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
                _killOnCloseSet = SetInformationJobObject(_hJob, JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation,
                    ref info, (uint)Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)));
            }

            public void Assign(Process process)
            {
                if (_hJob == IntPtr.Zero) return;
                try { AssignProcessToJobObject(_hJob, process.Handle); } catch { }
            }

            public void Terminate(uint exitCode)
            {
                if (_hJob == IntPtr.Zero) return;
                try { TerminateJobObject(_hJob, exitCode); } catch { }
            }

            public void Dispose()
            {
                if (_hJob != IntPtr.Zero)
                {
                    try { CloseHandle(_hJob); } catch { }
                    _hJob = IntPtr.Zero;
                }
            }

            // P/Invoke

            private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

            private enum JOBOBJECTINFOCLASS
            {
                JobObjectAssociateCompletionPortInformation = 7,
                JobObjectBasicLimitInformation = 2,
                JobObjectBasicUIRestrictions = 4,
                JobObjectEndOfJobTimeInformation = 6,
                JobObjectExtendedLimitInformation = 9,
                JobObjectSecurityLimitInformation = 5,
                JobObjectGroupInformation = 11
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                public long PerProcessUserTimeLimit;
                public long PerJobUserTimeLimit;
                public uint LimitFlags;
                public IntPtr MinimumWorkingSetSize;
                public IntPtr MaximumWorkingSetSize;
                public uint ActiveProcessLimit;
                public IntPtr Affinity;
                public uint PriorityClass;
                public uint SchedulingClass;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct IO_COUNTERS
            {
                public ulong ReadOperationCount;
                public ulong WriteOperationCount;
                public ulong OtherOperationCount;
                public ulong ReadTransferCount;
                public ulong WriteTransferCount;
                public ulong OtherTransferCount;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
                public IO_COUNTERS IoInfo;
                public IntPtr ProcessMemoryLimit;
                public IntPtr JobMemoryLimit;
                public IntPtr PeakProcessMemoryUsed;
                public IntPtr PeakJobMemoryUsed;
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool SetInformationJobObject(IntPtr hJob, JOBOBJECTINFOCLASS infoType,
                ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo, uint cbJobObjectInfoLength);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool TerminateJobObject(IntPtr hJob, uint uExitCode);

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool CloseHandle(IntPtr hObject);
        }
    }
}
