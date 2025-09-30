using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Forms;
using YtDlpGui.Models;
using YtDlpGui.Services;

namespace YtDlpGui
{
    public partial class MainWindow : Window
    {
        private readonly YtDlpService _service = new YtDlpService();
        private YtDlpInfo _info;
        private List<YtDlpFormat> _allFormats = new List<YtDlpFormat>();
        private CancellationTokenSource _cts;

        public MainWindow()
        {
            InitializeComponent();
            OutputFolder.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "yt-dlp");
            Directory.CreateDirectory(OutputFolder.Text);
            FormatsGrid.ItemsSource = _allFormats;

            Status("Готово");
        }

        private void Status(string text) => StatusText.Text = text;

        private async void FetchBtn_Click(object sender, RoutedEventArgs e)
        {
            var url = UrlTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                System.Windows.MessageBox.Show("Введите ссылку.");
                return;
            }

            Progress.Value = 0;
            DownloadBtn.IsEnabled = false;
            Status("Получение форматов...");

            try
            {
                _info = await _service.GetInfoAsync(YtDlpPath.Text, url);
                TitleText.Text = _info.title ?? "-";
                SourceText.Text = _info.webpage_url ?? url;

                _allFormats = (_info.formats ?? new List<YtDlpFormat>())
                    .OrderBy(f => ToResSortKey(f))
                    .ThenBy(f => f.ext)
                    .ToList();

                FormatsGrid.ItemsSource = _allFormats;
                FormatsGrid.Items.Refresh();

                FillFilters(_allFormats);
                FillSubs(_info);

                DownloadBtn.IsEnabled = _allFormats.Any();
                Status($"Найдено форматов: {_allFormats.Count}");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Ошибка: " + ex.Message, "yt-dlp", MessageBoxButton.OK, MessageBoxImage.Error);
                Status("Ошибка");
            }
        }

        private int ToResSortKey(YtDlpFormat f)
        {
            // Пытаемся упорядочить по высоте
            if (f.height.HasValue) return f.height.Value;
            var res = f.resolution;
            if (!string.IsNullOrWhiteSpace(res))
            {
                var m = Regex.Match(res, @"(\d+)[pPxX](\d+)?");
                if (m.Success && int.TryParse(m.Groups[1].Value, out int h)) return h;
                m = Regex.Match(res, @"(\d+)\s*x\s*(\d+)");
                if (m.Success && int.TryParse(m.Groups[2].Value, out int h2)) return h2;
            }
            return 0;
        }

        private void FillFilters(List<YtDlpFormat> list)
        {
            FillComboDistinct(ExtFilter, list.Select(x => x.ext));
            FillComboDistinct(ResFilter, list.Select(x => x.resolution ?? $"{x.width}x{x.height}"));
            FillComboDistinct(VCodecFilter, list.Select(x => x.vcodec));
            FillComboDistinct(ACodecFilter, list.Select(x => x.acodec));
            FillComboDistinct(FpsFilter, list.Select(x => x.fps?.ToString("0.#")));
        }

        private void FillSubs(YtDlpInfo info)
        {
            SubsLang.Items.Clear();
            var item = new ComboBoxItem { Content = "auto" };
            item.IsSelected = true;
            SubsLang.Items.Add(item);

            if (info?.subtitles != null)
            {
                foreach (var lang in info.subtitles.Keys.OrderBy(k => k))
                {
                    SubsLang.Items.Add(new ComboBoxItem { Content = lang });
                }
            }
        }

        private void FillComboDistinct(ComboBox combo, IEnumerable<string> values)
        {
            combo.Items.Clear();
            combo.Items.Add(new ComboBoxItem { Content = "Все", IsSelected = true });
            foreach (var v in values.Where(s => !string.IsNullOrWhiteSpace(s))
                                    .Select(s => s.Trim())
                                    .Distinct()
                                    .OrderBy(s => s))
            {
                combo.Items.Add(new ComboBoxItem { Content = v });
            }
        }

        private void Filter_Changed(object sender, SelectionChangedEventArgs e)
        {
            var ext = GetSelected(ExtFilter);
            var res = GetSelected(ResFilter);
            var vcodec = GetSelected(VCodecFilter);
            var acodec = GetSelected(ACodecFilter);
            var fps = GetSelected(FpsFilter);

            IEnumerable<YtDlpFormat> filtered = _allFormats;

            if (!string.IsNullOrEmpty(ext) && ext != "Все")
                filtered = filtered.Where(f => string.Equals(f.ext, ext, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(res) && res != "Все")
                filtered = filtered.Where(f => (f.resolution ?? $"{f.width}x{f.height}") == res);

            if (!string.IsNullOrEmpty(vcodec) && vcodec != "Все")
                filtered = filtered.Where(f => string.Equals(f.vcodec, vcodec, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(acodec) && acodec != "Все")
                filtered = filtered.Where(f => string.Equals(f.acodec, acodec, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(fps) && fps != "Все")
                filtered = filtered.Where(f => f.fps?.ToString("0.#") == fps);

            FormatsGrid.ItemsSource = filtered.ToList();
            FormatsGrid.Items.Refresh();
        }

        private static string GetSelected(ComboBox combo)
        {
            if (combo.SelectedItem is ComboBoxItem c)
                return c.Content?.ToString();
            return combo.SelectedItem?.ToString();
        }

        private void FormatsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DownloadBtn.IsEnabled)
                DownloadBtn_Click(sender, e);
        }

        private async void DownloadBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_info == null)
            {
                System.Windows.MessageBox.Show("Сначала получите форматы.");
                return;
            }
            var selected = FormatsGrid.SelectedItem as YtDlpFormat;
            if (selected == null)
            {
                System.Windows.MessageBox.Show("Выберите формат из списка.");
                return;
            }

            var folder = OutputFolder.Text;
            if (string.IsNullOrWhiteSpace(folder))
            {
                System.Windows.MessageBox.Show("Выберите папку сохранения.");
                return;
            }
            Directory.CreateDirectory(folder);

            var titleSafe = MakeSafeFileName(_info.title ?? _info.id ?? "video");
            var outTpl = Path.Combine(folder, "%(title)s [%(id)s].%(ext)s");

            // Формирование -f
            string formatExpr = selected.format_id;
            bool videoOnly = !string.IsNullOrWhiteSpace(selected.video_ext) && selected.audio_ext == "none";
            if (AutoMuxCheck.IsChecked == true && videoOnly)
            {
                var prefer = ((AudioPrefer.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "auto").ToLowerInvariant();
                var bestAudio = prefer == "auto" ? "bestaudio" : $"bestaudio[ext={prefer}]";
                formatExpr = $"{selected.format_id}+{bestAudio}/best";
            }

            // Субтитры
            var subsArgs = "";
            if (WriteSubs.IsChecked == true || EmbedSubs.IsChecked == true)
            {
                var lang = (SubsLang.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (string.IsNullOrWhiteSpace(lang) || lang == "auto") lang = "all";
                subsArgs += $" --sub-lang {lang} --write-sub";
                if (EmbedSubs.IsChecked == true) subsArgs += " --embed-subs";
            }

            var ffmpegPath = FfmpegPath.Text?.Trim();
            var ffmpegArg = !string.IsNullOrWhiteSpace(ffmpegPath) ? $" --ffmpeg-location \"{ffmpegPath}\"" : "";

            var args = $"-f \"{formatExpr}\" -o \"{outTpl}\"{subsArgs}{ffmpegArg} --no-progress --newline --no-warnings \"{_info.webpage_url}\"";

            _cts = new CancellationTokenSource();
            ToggleDownloading(true);

            Progress.Value = 0;
            Status("Загрузка...");
            try
            {
                await _service.DownloadAsync(YtDlpPath.Text, args, p =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        Progress.Value = p.Percent ?? 0;
                        Status(p.ToString());
                    });
                }, _cts.Token);

                Status("Готово");
            }
            catch (OperationCanceledException)
            {
                Status("Отменено");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Ошибка загрузки: " + ex.Message, "yt-dlp", MessageBoxButton.OK, MessageBoxImage.Error);
                Status("Ошибка");
            }
            finally
            {
                ToggleDownloading(false);
            }
        }

        private static string MakeSafeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private void ToggleDownloading(bool isDownloading)
        {
            DownloadBtn.IsEnabled = !isDownloading;
            CancelBtn.IsEnabled = isDownloading;
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
        }

        private void PickFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.SelectedPath = OutputFolder.Text;
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    OutputFolder.Text = dlg.SelectedPath;
                }
            }
        }

        private void PickYtDlp_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "yt-dlp|yt-dlp.exe|All files|*.*";
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    YtDlpPath.Text = dlg.FileName;
                }
            }
        }

        private void PickFfmpeg_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "ffmpeg|ffmpeg.exe|All files|*.*";
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    FfmpegPath.Text = dlg.FileName;
                }
            }
        }

        private void ThemeBtn_Click(object sender, RoutedEventArgs e)
        {
            // Простое переключение фона
            var bg = (Background as System.Windows.Media.SolidColorBrush)?.Color.ToString();
            if (bg == "#FF101114")
            {
                // Светлая
                this.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(246, 247, 250));
            }
            else
            {
                // Тёмная
                this.Background = (System.Windows.Media.Brush)FindResource("BgBrush");
            }
        }

        private void AboutBtn_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.MessageBox.Show("yt-dlp GUI для .NET Framework 4.7.2\nБез сторонних UI-библиотек.\nПарсинг: JavaScriptSerializer (есть задел под System.Text.Json).", "О программе");
        }
    }
}
