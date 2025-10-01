using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using YtDlpGui.Wpf.Models;

namespace YtDlpGui.Wpf.Services
{
    public class PresetService
    {
        private string FilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "presets.json");

        public async Task<PresetStore> LoadAsync()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    var def = CreateDefault();
                    await SaveAsync(def).ConfigureAwait(false);
                    return def;
                }

                // Читаем асинхронно через StreamReader (совместимо с .NET Framework)
                string json;
                using (var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
                using (var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                {
                    json = await sr.ReadToEndAsync().ConfigureAwait(false);
                }

                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var store = ser.Deserialize<PresetStore>(json) ?? new PresetStore();
                if (store.Items == null) store.Items = new System.Collections.Generic.List<Preset>();
                if (store.Items.Count == 0)
                {
                    var def = CreateDefault();
                    await SaveAsync(def).ConfigureAwait(false);
                    return def;
                }
                return store;
            }
            catch
            {
                var def = CreateDefault();
                await SaveAsync(def).ConfigureAwait(false);
                return def;
            }
        }

        public async Task SaveAsync(PresetStore store)
        {
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var json = ser.Serialize(store);

            // Записываем асинхронно через StreamWriter
            using (var fs = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            using (var sw = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                await sw.WriteAsync(json).ConfigureAwait(false);
            }
        }

        public void MarkDefault(PresetStore store, string name)
        {
            if (store?.Items == null) return;
            foreach (var p in store.Items)
                p.IsDefault = string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase);
        }

        public static PresetStore CreateDefault()
        {
            return new PresetStore
            {
                Items =
                {
                    new Preset { Name = "mp4",  Args = "-f bestvideo[ext=mp4]+bestaudio/best --merge-output-format mp4", IsDefault = true },
                    new Preset { Name = "mp3",  Args = "-f bestaudio --extract-audio --audio-format mp3", IsDefault = false },
                    new Preset { Name = "webm", Args = "-f bestvideo[ext=webm]+bestaudio/best --merge-output-format webm", IsDefault = false },
                }
            };
        }
    }
}
