using System.Collections.Generic;

namespace YtDlpGui.Wpf.Models
{
    public class YtDlpInfo
    {
        public string id { get; set; }
        public string title { get; set; }
        public string description { get; set; }
        public string webpage_url { get; set; }
        public string thumbnail { get; set; }
        public List<Format> formats { get; set; }
        public Dictionary<string, List<Subtitle>> subtitles { get; set; }
    }

    public class Format
    {
        public string format_id { get; set; }
        public string ext { get; set; }
        public int? width { get; set; }
        public int? height { get; set; }
        public double? tbr { get; set; }
        public double? fps { get; set; }
        public string format_note { get; set; }
        public string container { get; set; }
        public string vcodec { get; set; }
        public string acodec { get; set; }
        public string protocol { get; set; }
        public string audio_ext { get; set; }
        public string video_ext { get; set; }
        public string resolution { get; set; }
        public string url { get; set; }
        public string format { get; set; }
    }

    public class Subtitle
    {
        public string ext { get; set; }
        public string url { get; set; }
    }

    public class FormatDisplay
    {
        public Format Model { get; set; }
        public string Display { get; set; }
    }
}
