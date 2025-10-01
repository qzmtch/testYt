using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YtDlpGui.Wpf.Models;
using YtDlpGui.Wpf.Services;
using WinForms = System.Windows.Forms;

namespace YtDlpGui.Wpf
{
    public partial class MainWindow : Window
    {
        private readonly YtDlpService _svc = new YtDlpService();
        private readonly PresetService _presetSvc = new PresetService();

        private YtDlpInfo _info;
        private List<Format> _allFormats = new List<Format>();
        private List<FormatDisplay> _currentDisplay = new List<FormatDisplay>();
        private PresetStore _presetStore = new PresetStore();

        private CancellationTokenSource _cts;
        private bool _uiReady;

        public string TitleText { get; set; }
        public string WebpageUrl { get; set; }
        public string Description { get; set; }

        public MainWindow()
        {
            InitializeComponent();

            // дефолтная папка — рабочий стол (ничего не создаём)
            OutputFolderText.Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

            this.Loaded += async (_, __) =>
            {
                await LoadPresetsAsync();

                // инициализируем видимость блоков базового режима
                DownloadTypeChanged(null, null);

                _uiReady = true;
            };
        }

        // ---------- Пресеты ----------

        private async Task LoadPresetsAsync()
        {
            _presetStore = await _presetSvc.LoadAsync();
            PresetsCombo.ItemsSource = _presetStore.Items;
            var def = _presetStore.Items.FirstOrDefault(p => p.IsDefault) ?? _presetStore.Items.FirstOrDefault();
            if (def != null)
            {
                PresetsCombo.SelectedItem = def;
                PresetNameText.Text = def.Name;
                PresetArgsText.Text = def.Args;
            }
        }

        private async void PresetsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_uiReady) return;
            var p = PresetsCombo.SelectedItem as Preset;
            if (p == null) return;

            PresetNameText.Text = p.Name;
            PresetArgsText.Text = p.Args;

            _presetSvc.MarkDefault(_presetStore, p.Name);
            await _presetSvc.SaveAsync(_presetStore);
        }

        private async void SavePreset_Click(object sender, RoutedEventArgs e)
        {
            var name = (PresetNameText.Text ?? "").Trim();
            var args = (PresetArgsText.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Введите имя пресета.", "Пресеты", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var existing = _presetStore.Items.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = new Preset { Name = name, Args = args, IsDefault = true };
                foreach (var i in _presetStore.Items) i.IsDefault = false;
                _presetStore.Items.Add(existing);
            }
            else
            {
                existing.Args = args;
                foreach (var i in _presetStore.Items) i.IsDefault = false;
                existing.IsDefault = true;
            }

            await _presetSvc.SaveAsync(_presetStore);

            PresetsCombo.ItemsSource = null;
            PresetsCombo.ItemsSource = _presetStore.Items;
            PresetsCombo.SelectedItem = existing;
            StatusText.Text = "Пресет сохранён и установлен по умолчанию.";
        }

        private async void DownloadPreset_Click(object sender, RoutedEventArgs e)
        {
            var url = UrlText.Text?.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show("Введите ссылку.", "yt-dlp GUI", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var args = (PresetArgsText.Text ?? "").Trim();
            var outDir = OutputFolderText.Text?.Trim();
            if (string.IsNullOrWhiteSpace(outDir)) outDir = Environment.CurrentDirectory;
            try { Directory.CreateDirectory(outDir); } catch { }

            string template = Path.Combine(outDir, "%(title)s [%(id)s].%(ext)s");
            bool ignoreCfg = IgnoreConfigCheck.IsChecked == true;

            DownloadBtn.IsEnabled = false;
            CancelBtn.IsEnabled = true;
            StatusText.Text = "Загрузка (пресет)...";
            ProgressBar.Value = 0;
            ProgressText.Text = "";

            try
            {
                _svc.YtDlpPath = string.IsNullOrWhiteSpace(YtDlpPathText.Text) ? "yt-dlp.exe" : YtDlpPathText.Text.Trim();
                using (_cts = new CancellationTokenSource())
                {
                    var prog = new Progress<(double p, string line)>(t =>
                    {
                        if (!double.IsNaN(t.p)) ProgressBar.Value = Math.Max(0, Math.Min(100, t.p));
                        ProgressText.Text = t.line;
                    });

                    int code = await _svc.DownloadWithArgsAsync(url, args, template, ignoreCfg, prog, _cts.Token);
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

        private void SortPresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_uiReady) return;
            var content = (SortPresetCombo.SelectedItem as ComboBoxItem)?.Content as string;
            if (string.IsNullOrWhiteSpace(content) || content == "(нет)") { SortText.Text = ""; return; }
            SortText.Text = content;
        }

        // ---------- Получение информации ----------

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
                bool ignoreCfg = IgnoreConfigCheck.IsChecked == true;

                using (_cts = new CancellationTokenSource())
                {
                    _info = await _svc.GetInfoAsync(url, ignoreCfg, _cts.Token);
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

        // ---------- Базовый режим (тип/качество/форматы) ----------

        private void DownloadTypeChanged(object sender, RoutedEventArgs e)
        {
            if (CustomTypeRadio.IsChecked == true)
            {
                CustomFormatPanel.Visibility = Visibility.Visible;
                AddAudioCheck.Visibility = Visibility.Collapsed;
                VideoFormatCombo.IsEnabled = false;
                AudioFormatCombo.IsEnabled = true;
            }
            else if (AudioTypeRadio.IsChecked == true)
            {
                CustomFormatPanel.Visibility = Visibility.Collapsed;
                AddAudioCheck.Visibility = Visibility.Collapsed;
                VideoFormatCombo.IsEnabled = false;
                AudioFormatCombo.IsEnabled = true;
            }
            else // Видео
            {
                CustomFormatPanel.Visibility = Visibility.Collapsed;
                AddAudioCheck.Visibility = Visibility.Visible;
                VideoFormatCombo.IsEnabled = true;
                AudioFormatCombo.IsEnabled = true;
            }
        }

        private string BuildSimpleFormatSelector()
        {
            // Кастомный режим: берём как есть
            if (CustomTypeRadio.IsChecked == true)
            {
                var t = (CustomFormatText.Text ?? "").Trim();
                return string.IsNullOrWhiteSpace(t) ? "best" : t;
            }

            // helpers
            string GetSelected(ComboBox cb)
            {
                if (cb.SelectedItem is ComboBoxItem cbi) return (cbi.Content as string) ?? "best";
                return (cb.Text ?? "best").Trim();
            }
            int ParseHeight(string s)
            {
                if (string.Equals(s, "best", StringComparison.OrdinalIgnoreCase)) return 0;
                if (s != null && s.EndsWith("p", StringComparison.OrdinalIgnoreCase) && int.TryParse(s.TrimEnd('p', 'P'), out var p)) return p;
                return 0;
            }
            string AudioExtForVideo(string vext)
            {
                switch ((vext ?? "").ToLowerInvariant())
                {
                    case "mp4": return "m4a";
                    case "mov": return "m4a";
                    case "webm": return "webm"; // как правило opus внутри webm
                    default: return null;
                }
            }

            // Аудио-режим: bestaudio или bestaudio[ext=...]
            if (AudioTypeRadio.IsChecked == true)
            {
                var aextSel = GetSelected(AudioFormatCombo).ToLowerInvariant();
                if (aextSel == "best") return "bestaudio";
                return $"bestaudio[ext={aextSel}]";
            }

            // Видео-режим
            var vextSel = GetSelected(VideoFormatCombo).ToLowerInvariant(); // best/mp4/webm/...
            var qSel = GetSelected(QualityCombo).ToLowerInvariant();        // best/1080p/...
            var h = ParseHeight(qSel);
            bool addAudio = AddAudioCheck.IsChecked == true;

            // bestvideo-подобный
            string v = "bv*";
            if (vextSel != "best") v += $"[ext={vextSel}]";
            if (h > 0) v += $"[height<={h}]";

            if (addAudio)
            {
                var aextSel = GetSelected(AudioFormatCombo).ToLowerInvariant();
                if (aextSel == "best")
                    aextSel = AudioExtForVideo(vextSel) ?? "best";

                string a = (aextSel == "best") ? "ba" : $"ba[ext={aextSel}]";

                // fallback: комбинированный b с теми же ограничениями
                string b = "b";
                if (vextSel != "best") b += $"[ext={vextSel}]";
                if (h > 0) b += $"[height<={h}]";

                return $"{v}+{a}/{b}";
            }
            else
            {
                string b = "b";
                if (vextSel != "best") b += $"[ext={vextSel}]";
                if (h > 0) b += $"[height<={h}]";

                return $"{v}/{b}";
            }
        }

        // ---------- Фильтры / форматы (расширенный режим) ----------

        private void BuildFilters()
        {
            var exts = _allFormats.Select(f => f.ext).Where(s => !string.IsNullOrWhiteSpace(s))
                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                  .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();

            ExtCombo.Items.Clear();
            ExtCombo.Items.Add(new ComboBoxItem { Content = "Все", IsSelected = true });
            foreach (var e in exts) ExtCombo.Items.Add(e);

            var res = _allFormats.Select(f =>
                        f.height.HasValue ? $"{f.height.Value}p" :
                        !string.IsNullOrWhiteSpace(f.resolution) ? f.resolution : null)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(s => ParseResOrder(s)).ToList();

            ResCombo.Items.Clear();
            ResCombo.Items.Add(new ComboBoxItem { Content = "Любое разрешение", IsSelected = true });
            foreach (var r in res) ResCombo.Items.Add(r);
        }

        private static int ParseResOrder(string s)
        {
            if (!string.IsNullOrEmpty(s) && s.EndsWith("p") && int.TryParse(s.TrimEnd('p'), out var p))
                return p;
            return int.MinValue;
        }

        private static string GetComboSelectedText(ComboBox cb)
        {
            if (cb == null) return null;
            if (cb.SelectedItem is ComboBoxItem cbi) return cbi.Content as string;
            if (cb.SelectedItem is string s1) return s1;
            return cb.Text;
        }

        private void ApplyFilters()
        {
            if (!_uiReady) return;
            if (_allFormats == null) return;

            string ext = GetComboSelectedText(ExtCombo);
            string proto = GetComboSelectedText(ProtoCombo);
            string res = GetComboSelectedText(ResCombo);

            var q = _allFormats.AsEnumerable();

            if (FilterVideo.IsChecked == true)
                q = q.Where(f => !string.Equals(f.video_ext, "none", StringComparison.OrdinalIgnoreCase));
            else if (FilterAudio.IsChecked == true)
                q = q.Where(f => string.Equals(f.video_ext, "none", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(ext) && !string.Equals(ext, "Все", StringComparison.OrdinalIgnoreCase))
                q = q.Where(f => string.Equals(f.ext, ext, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(proto) && !string.Equals(proto, "Любой протокол", StringComparison.OrdinalIgnoreCase))
                q = q.Where(f => string.Equals(f.protocol, proto, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(res) && !string.Equals(res, "Любое разрешение", StringComparison.OrdinalIgnoreCase))
            {
                q = q.Where(f =>
                    (f.height.HasValue && $"{f.height.Value}p".Equals(res, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(f.resolution) && f.resolution.Equals(res, StringComparison.OrdinalIgnoreCase)));
            }

            var list = q.Select(f => new FormatDisplay
            {
                Model = f,
                Display = MakeDisplay(f)
            }).ToList();

            _currentDisplay = list;
            FormatsList.ItemsSource = _currentDisplay;
        }

        private static string MakeDisplay(Format f)
        {
            string kind = (f.video_ext != "none" && f.audio_ext != "none") ? "AV" :
                          (f.video_ext != "none") ? "V" :
                          (f.audio_ext != "none") ? "A" : "?";
            string res = f.height.HasValue ? $"{f.height.Value}p" : (f.resolution ?? "");
            string fps = f.fps.HasValue ? $"{f.fps.Value:0.#}fps" : "";
            string br = f.tbr.HasValue ? $"{f.tbr.Value:0.#}kbps" : "";
            string v = string.IsNullOrWhiteSpace(f.vcodec) ? "-" : f.vcodec;
            string a = string.IsNullOrWhiteSpace(f.acodec) ? "-" : f.acodec;
            string proto = f.protocol ?? "-";
            return $"{f.format_id}  [{kind}]  {f.ext}  {res} {fps}  {v}/{a}  {br}  ({proto})";
        }

        private void FilterChanged(object sender, RoutedEventArgs e)
        {
            if (!_uiReady) return;
            ApplyFilters();
        }

        private void BuildSubtitles()
        {
            SubsItems.ItemsSource = null;
            if (_info?.subtitles == null || _info.subtitles.Count == 0) return;
            var langs = _info.subtitles.Keys.ToList();
            SubsItems.ItemsSource = langs;
        }

        // ---------- Селектор форматов ----------

        private string BuildFormatSelectorFromSelection(out bool useCommaTemplate)
        {
            useCommaTemplate = false;

            var selected = FormatsList?.SelectedItems?.Cast<FormatDisplay>().ToList() ?? new List<FormatDisplay>();
            if (selected.Count == 0) return "best";

            string joiner;
            if (SeparateCommaRadio.IsChecked == true)
            {
                joiner = ",";
                useCommaTemplate = selected.Count > 1;
            }
            else
            {
                joiner = "+";
            }

            var ids = selected.Select(s => s.Model.format_id).Where(id => !string.IsNullOrWhiteSpace(id));
            return string.Join(joiner, ids);
        }

        // ---------- Загрузка ----------

        private async void DownloadBtn_Click(object sender, RoutedEventArgs e)
        {
            var url = UrlText.Text?.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                MessageBox.Show("Введите ссылку.", "yt-dlp GUI", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var outDir = OutputFolderText.Text?.Trim();
            if (string.IsNullOrWhiteSpace(outDir)) outDir = Environment.CurrentDirectory;
            try { Directory.CreateDirectory(outDir); } catch { }

            // Если выбран расширенный список — используем его, иначе базовый режим
            bool useCommaTemplate;
            string selector;
            if (FormatsList?.SelectedItems?.Count > 0)
                selector = BuildFormatSelectorFromSelection(out useCommaTemplate);
            else
            {
                selector = BuildSimpleFormatSelector();
                useCommaTemplate = selector.Contains(",");
            }

            string template = useCommaTemplate
                ? Path.Combine(outDir, "%(title)s.f%(format_id)s.%(ext)s")
                : Path.Combine(outDir, "%(title)s [%(id)s].%(ext)s");

            var checkedLangs = GetCheckedSubLangs();
            bool writeSubs = checkedLangs.Count > 0;
            string subLangs = string.Join(",", checkedLangs);

            bool ignoreCfg = IgnoreConfigCheck.IsChecked == true;
            bool vMulti = VideoMultistreamsCheck.IsChecked == true;
            bool aMulti = AudioMultistreamsCheck.IsChecked == true;
            string sort = (SortText.Text ?? "").Trim();

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
                        if (!double.IsNaN(t.p)) ProgressBar.Value = Math.Max(0, Math.Min(100, t.p));
                        ProgressText.Text = t.line;
                    });

                    int code = await _svc.DownloadAsyncAdv(
                        url,
                        QuoteIfNeeded(selector),
                        template,
                        subLangs,
                        writeSubs,
                        ignoreCfg,
                        sort,
                        vMulti,
                        aMulti,
                        prog,
                        _cts.Token);

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

        private void CancelBtn_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        // ---------- Прочее: диалоги/копирование/поиск контролов ----------

        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new WinForms.FolderBrowserDialog())
            {
                dlg.SelectedPath = string.IsNullOrWhiteSpace(OutputFolderText.Text)
                    ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                    : OutputFolderText.Text;
                if (dlg.ShowDialog() == WinForms.DialogResult.OK)
                    OutputFolderText.Text = dlg.SelectedPath;
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var path = OutputFolderText.Text?.Trim();
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                System.Diagnostics.Process.Start("explorer.exe", path);
        }

        private void CopyCmd_Click(object sender, RoutedEventArgs e)
        {
            var url = UrlText.Text?.Trim();
            if (string.IsNullOrWhiteSpace(url)) return;

            bool useCommaTemplate;
            string selector;
            if (FormatsList?.SelectedItems?.Count > 0)
                selector = BuildFormatSelectorFromSelection(out useCommaTemplate);
            else
            {
                selector = BuildSimpleFormatSelector();
                useCommaTemplate = selector.Contains(",");
            }

            var outDir = string.IsNullOrWhiteSpace(OutputFolderText.Text) ? Environment.CurrentDirectory : OutputFolderText.Text.Trim();
            string template = useCommaTemplate
                ? Path.Combine(outDir, "%(title)s.f%(format_id)s.%(ext)s")
                : Path.Combine(outDir, "%(title)s [%(id)s].%(ext)s");

            bool ignoreCfg = IgnoreConfigCheck.IsChecked == true;
            bool vMulti = VideoMultistreamsCheck.IsChecked == true;
            bool aMulti = AudioMultistreamsCheck.IsChecked == true;
            string sort = (SortText.Text ?? "").Trim();

            var checkedLangs = GetCheckedSubLangs();
            bool writeSubs = checkedLangs.Count > 0;
            string subLangs = string.Join(",", checkedLangs);

            var sb = new System.Text.StringBuilder();
            sb.Append("yt-dlp ");
            if (ignoreCfg) sb.Append("--ignore-config ");
            sb.Append("--newline --no-color ");
            if (!string.IsNullOrWhiteSpace(sort)) sb.Append($"-S \"{sort}\" ");
            if (vMulti) sb.Append("--video-multistreams ");
            if (aMulti) sb.Append("--audio-multistreams ");

            sb.Append($"-f {QuoteIfNeeded(selector)} ");
            if (writeSubs && !string.IsNullOrWhiteSpace(subLangs))
                sb.Append($"--write-subs --sub-langs \"{subLangs}\" ");
            sb.Append($"-o \"{template}\" ");
            sb.Append($"\"{url}\"");

            try { Clipboard.SetText(sb.ToString()); } catch { }
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

        private static string QuoteIfNeeded(string t)
            => string.IsNullOrEmpty(t) ? t
               : (t.Contains(" ") || t.Contains("+") || t.Contains(",") || t.Contains("[") || t.Contains("]")) ? $"\"{t}\"" : t;

        private List<string> GetCheckedSubLangs()
        {
            var res = new List<string>();
            foreach (var cb in FindVisualChildren<CheckBox>(SubsItems))
                if (cb.IsChecked == true && cb.Content != null) res.Add(cb.Content.ToString());
            return res.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) yield break;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t) yield return t;
                foreach (var d in FindVisualChildren<T>(child)) yield return d;
            }
        }
    }
}
