using System;
using System.Collections.Generic;

namespace YtDlpGui.Models
{
    public class YtDlpInfo
    {
        public string id { get; set; }
        public string title { get; set; }
        public string webpage_url { get; set; }
        public List<YtDlpFormat> formats { get; set; }
        public Dictionary<string, List<YtDlpSubtitle>> subtitles { get; set; }
    }

    public class YtDlpFormat
    {
        public string format_id { get; set; }
        public string format { get; set; }
        public string format_note { get; set; }
        public string ext { get; set; }

        public int? width { get; set; }
        public int? height { get; set; }
        public string resolution { get; set; }
        public double? fps { get; set; }

        public string vcodec { get; set; }
        public string acodec { get; set; }
        public string video_ext { get; set; }
        public string audio_ext { get; set; }
        public string container { get; set; }
        public string protocol { get; set; }
        public string dynamic_range { get; set; }

        public double? tbr { get; set; }
        public double? vbr { get; set; }
        public double? abr { get; set; }
    }

    public class YtDlpSubtitle
    {
        public string ext { get; set; }
        public string url { get; set; }
    }

    public class YtDlpProgress
    {
        public double? Percent { get; set; }
        public string Speed { get; set; }
        public string Eta { get; set; }
        public override string ToString()
        {
            var p = Percent.HasValue ? Percent.Value.ToString("0.0") + "%" : "";
            return $"[download] {p} {Speed ?? ""} {(!string.IsNullOrEmpty(Eta) ? "ETA " + Eta : "")}".Trim();
        }
    }
}
