using System.Collections.Generic;

namespace YtDlpGui.Wpf.Models
{
    public class Preset
    {
        public string Name { get; set; }
        public string Args { get; set; } // любые аргументы yt-dlp ДО URL (мы сами добавим URL и -o)
        public bool IsDefault { get; set; }
    }

    public class PresetStore
    {
        public List<Preset> Items { get; set; } = new List<Preset>();
    }
}
