using System.Collections.Generic;

namespace YTDlpGui.Models
{
    public class YtDlpRoot
    {
        public string id { get; set; }
        public string title { get; set; }
        public string description { get; set; }
        public string thumbnail { get; set; }
        public string webpage_url { get; set; }
        public List<Format> formats { get; set; }
        public Dictionary<string, List<SubtitleTrack>> subtitles { get; set; }
    }

    public class Format
    {
        public string format_id { get; set; }
        public string ext { get; set; }
        public int? width { get; set; }
        public int? height { get; set; }
        public double? tbr { get; set; }
        public int? fps { get; set; }
        public string format_note { get; set; }
        public string vcodec { get; set; }
        public string acodec { get; set; }
        public string audio_ext { get; set; }
        public string video_ext { get; set; }
        public string resolution { get; set; } // иногда уже готовая строка
    }

    public class SubtitleTrack
    {
        public string ext { get; set; }
        public string url { get; set; }
    }

    // Для UI
    public class FormatView
    {
        public string FormatId { get; set; }
        public string Ext { get; set; }
        public string Resolution { get; set; }
        public string Fps { get; set; }
        public string VCodec { get; set; }
        public string ACodec { get; set; }
        public string Tbr { get; set; }
        public string Note { get; set; }
        public bool IsAudioOnly { get; set; }
        public bool IsVideoOnly { get; set; }
    }
}
