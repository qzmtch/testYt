using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using YtDlpGui.Wpf.Models;
using YtDlpGui.Wpf.Services;

namespace YtDlpGui.Wpf
{
    public partial class MainWindow : Window
    {
        private readonly YtDlpService _svc = new YtDlpService();
        private YtDlpInfo _info;
        private List<Format> _allFormats = new List<Format>();
        private List<FormatDisplay> _currentDisplay = new List<FormatDisplay>();
        private CancellationTokenSource _cts;

        public string TitleText { get; set; }
        public string WebpageUrl { get; set; }
        public string Description { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            AutoMergeCheck.IsChecked = true;
            OutputFolderText.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Downloads");
            Directory.CreateDirectory(OutputFolderText.Text);
        }

        private async void FetchBtn_Click(object sender, RoutedEventArgs e)
        {
            var url = UrlText.Text?.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show("Введите ссылку.", "yt-dlp GUI", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            FetchBtn.IsEnabled = false;
            StatusText.Text = "Получение информации...";
            ProgressBar.IsIndeterminate = true;
            try
            {
                _svc.YtDlpPath = string.IsNullOrWhiteSpace(YtDlpPathText.Text) ? "yt-dlp.exe" : YtDlpPathText.Text.Trim();

                using (_cts = new CancellationTokenSource())
                {
                    _info = await _svc.GetInfoAsync(url, _cts.Token);
                }

                TitleText = _info.title;
                WebpageUrl = _info.webpage_url;
                Description = _info.description;
                DataContext = new { Title = TitleText, WebpageUrl = WebpageUrl, Description = Description };
                await LoadThumbnailAsync(_info.thumbnail);

                _allFormats = _info.formats ?? new List<Format>();
                BuildFilters();
                ApplyFilters();
                BuildSubtitles();

                StatusText.Text = "Информация получена.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Ошибка.";
            }
            finally
            {
                ProgressBar.IsIndeterminate = false;
                FetchBtn.IsEnabled = true;
            }
        }

        private async Task LoadThumbnailAsync(string url)
        {
            ThumbImage.Source = null;
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                using (var wc = new WebClient())
                {
                    var data = await wc.DownloadDataTaskAsync(url);
                    var bmp = new BitmapImage();
                    using (var ms = new MemoryStream(data))
                    {
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                        bmp.Freeze();
                    }
                    ThumbImage.Source = bmp;
                }
            }
            catch { }
        }

        private void BuildFilters()
        {
            var exts = _allFormats.Select(f => f.ext).Where(s => !string.IsNullOrWhiteSpace(s))
                                  .Distinct().OrderBy(s => s).ToList();
            ExtCombo.Items.Clear();
            ExtCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "Все", IsSelected = true });
            foreach (var e in exts) ExtCombo.Items.Add(e);

            var res = _allFormats.Select(f =>
                         f.height.HasValue ? $"{f.height.Value}p" :
                         !string.IsNullOrWhiteSpace(f.resolution) ? f.resolution : null)
                         .Where(s => !string.IsNullOrWhiteSpace(s))
                         .Distinct()
                         .OrderBy(s => ParseResOrder(s))
                         .ToList();

            ResCombo.Items.Clear();
            ResCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "Любое разрешение", IsSelected = true });
            foreach (var r in res) ResCombo.Items.Add(r);
        }

        private static int ParseResOrder(string s)
        {
            if (s != null && s.EndsWith("p") && int.TryParse(s.TrimEnd('p'), out var p)) return p;
            return -1;
        }

        private void ApplyFilters()
        {
            if (_allFormats == null) return;

            string ext = (ExtCombo.SelectedItem as string) ?? (ExtCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content as string;
            string proto = (ProtoCombo.SelectedItem as string) ?? (ProtoCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content as string;
            string res = (ResCombo.SelectedItem as string) ?? (ResCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content as string;

            var q = _allFormats.AsEnumerable();

            if (FilterVideo.IsChecked == true)
                q = q.Where(f => f.video_ext != "none");
            else if (FilterAudio.IsChecked == true)
                q = q.Where(f => f.video_ext == "none");

            if (!string.IsNullOrWhiteSpace(ext) && ext != "Все")
                q = q.Where(f => string.Equals(f.ext, ext, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(proto) && proto != "Любой протокол")
                q = q.Where(f => string.Equals(f.protocol, proto, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(res) && res != "Любое разрешение")
            {
                q = q.Where(f =>
                    (f.height.HasValue && $"{f.height.Value}p" == res) ||
                    string.Equals(f.resolution, res, StringComparison.OrdinalIgnoreCase));
            }

            var list = q.Select(f => new FormatDisplay
            {
                Model = f,
                Display = MakeDisplay(f)
            }).ToList();

            _currentDisplay = list;
            FormatsCombo.ItemsSource = _currentDisplay;
            if (_currentDisplay.Count > 0)
                FormatsCombo.SelectedIndex = 0;
        }

        private static string MakeDisplay(Format f)
        {
            string kind = (f.video_ext != "none" && f.audio_ext != "none") ? "AV" :
                          (f.video_ext != "none") ? "V" :
                          (f.audio_ext != "none") ? "A" : "?";
            string res = f.height.HasValue ? $"{f.height.Value}p" : (f.resolution ?? "");
            string fps = f.fps.HasValue ? $"{f.fps.Value:0.#}fps" : "";
            string br = f.tbr.HasValue ? $"{f.tbr.Value:0.#}kbps" : "";
            return $"{f.format_id}  [{kind}]  {f.ext}  {res} {fps}  {f.vcodec}/{f.acodec}  {br}  ({f.protocol})";
        }

        private void BuildSubtitles()
        {
            SubsItems.ItemsSource = null;
            if (_info?.subtitles == null || _info.subtitles.Count == 0) return;
            var langs = _info.subtitles.Keys.ToList();
            SubsItems.ItemsSource = langs;
        }

        private void FilterChanged(object sender, RoutedEventArgs e) => ApplyFilters();

        private void FormatsCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
        }

        private string BuildFormatSelector()
        {
            var sel = FormatsCombo.SelectedItem as FormatDisplay;
            if (sel == null) return "best";

            var f = sel.Model;

            if (AutoMergeCheck.IsChecked == true && f.video_ext != "none" && f.audio_ext == "none")
            {
                var height = f.height ?? ParseHeightFromRes(f.resolution);
                if (height > 0)
                    return $"bestvideo[height={height}][ext={f.ext}]+bestaudio/best";
                return $"bestvideo[ext={f.ext}]+bestaudio/best";
            }

            if (AutoMergeCheck.IsChecked == true && f.video_ext == "none" && f.audio_ext != "none")
            {
                return f.format_id;
            }

            return f.format_id;
        }

        private static int ParseHeightFromRes(string res)
        {
            if (string.IsNullOrWhiteSpace(res)) return 0;
            if (res.EndsWith("p") && int.TryParse(res.TrimEnd('p'), out var p)) return p;
            var parts = res.Split('x');
            if (parts.Length == 2 && int.TryParse(parts[1], out var h)) return h;
            return 0;
        }

        private async void DownloadBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_info == null)
            {
                MessageBox.Show("Сначала получите информацию о видео.", "yt-dlp GUI", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sel = FormatsCombo.SelectedItem as FormatDisplay;
            if (sel == null)
            {
                MessageBox.Show("Выберите формат.", "yt-dlp GUI", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var url = UrlText.Text?.Trim();
            var outDir = OutputFolderText.Text?.Trim();
            if (string.IsNullOrWhiteSpace(outDir)) outDir = Environment.CurrentDirectory;
            Directory.CreateDirectory(outDir);

            string selector = BuildFormatSelector();
            string template = Path.Combine(outDir, "%(title)s [%(id)s].%(ext)s");

            var checkedLangs = GetCheckedSubLangs();
            bool writeSubs = checkedLangs.Count > 0;
            string subLangs = string.Join(",", checkedLangs);

            DownloadBtn.IsEnabled = false;
            CancelBtn.IsEnabled = true;
            StatusText.Text = "Загрузка...";
            ProgressBar.Value = 0;
            ProgressText.Text = "";

            try
            {
                _svc.YtDlpPath = string.IsNullOrWhiteSpace(YtDlpPathText.Text) ? "yt-dlp.exe" : YtDlpPathText.Text.Trim();
                using (_cts = new CancellationTokenSource())
                {
                    var prog = new Progress<(double p, string line)>(t =>
                    {
                        if (!double.IsNaN(t.p)) ProgressBar.Value = t.p;
                        ProgressText.Text = t.line;
                    });

                    int code = await _svc.DownloadAsync(url, QuoteIfNeeded(selector), template, subLangs, writeSubs, prog, _cts.Token);
                    if (code == 0)
                    {
                        StatusText.Text = "Готово.";
                        ProgressText.Text = "Загрузка завершена";
                    }
                    else
                    {
                        StatusText.Text = $"yt-dlp завершился с кодом {code}";
                        MessageBox.Show($"yt-dlp завершился с кодом {code}", "Загрузка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Отменено.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Ошибка.";
                MessageBox.Show(ex.Message, "Ошибка загрузки", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                DownloadBtn.IsEnabled = true;
                CancelBtn.IsEnabled = false;
            }
        }

        private List<string> GetCheckedSubLangs()
        {
            var res = new List<string>();
            foreach (var item in SubsItems.Items)
            {
                var cp = (System.Windows.Controls.ContentPresenter)SubsItems.ItemContainerGenerator.ContainerFromItem(item);
                if (cp == null) continue;
                var cb = FindVisualChild<System.Windows.Controls.CheckBox>(cp);
                if (cb != null && cb.IsChecked == true) res.Add(cb.Content?.ToString());
            }
            return res;
        }

        private static string QuoteIfNeeded(string t)
            => (t.Contains(" ") || t.Contains("+") || t.Contains("[") || t.Contains("]")) ? $"\"{t}\"" : t;

        private void CancelBtn_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.SelectedPath = string.IsNullOrWhiteSpace(OutputFolderText.Text) ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop) : OutputFolderText.Text;
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    OutputFolderText.Text = dlg.SelectedPath;
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var path = OutputFolderText.Text?.Trim();
            if (Directory.Exists(path))
                System.Diagnostics.Process.Start("explorer.exe", path);
        }

        private void CopyCmd_Click(object sender, RoutedEventArgs e)
        {
            var url = UrlText.Text?.Trim();
            string selector = BuildFormatSelector();
            var outDir = string.IsNullOrWhiteSpace(OutputFolderText.Text) ? Environment.CurrentDirectory : OutputFolderText.Text.Trim();
            string template = Path.Combine(outDir, "%(title)s [%(id)s].%(ext)s");

            var checkedLangs = GetCheckedSubLangs();
            bool writeSubs = checkedLangs.Count > 0;
            string subLangs = string.Join(",", checkedLangs);

            var sb = new System.Text.StringBuilder();
            sb.Append("yt-dlp ");
            sb.Append($"-f {QuoteIfNeeded(selector)} ");
            if (writeSubs && !string.IsNullOrWhiteSpace(subLangs))
                sb.Append($"--write-subs --sub-langs \"{subLangs}\" ");
            sb.Append("--ignore-config ");
            sb.Append($"-o \"{template}\" ");
            sb.Append($"\"{url}\"");

            Clipboard.SetText(sb.ToString());
            StatusText.Text = "Команда скопирована.";
        }

        private void BrowseYtDlp_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "yt-dlp|yt-dlp.exe|Все файлы|*.*",
                Title = "Укажите yt-dlp.exe"
            };
            if (ofd.ShowDialog() == true)
                YtDlpPathText.Text = ofd.FileName;
        }

        private static T FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
        {
            if (obj == null) return null;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(obj, i);
                if (child is T t) return t;
                var res = FindVisualChild<T>(child);
                if (res != null) return res;
            }
            return null;
        }
    }
}
