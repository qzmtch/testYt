using System;
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
        // Могут быть и другие поля — не обязательны для нашей логики
    }

    public class Format
    {
        public string format_id { get; set; }
        public string ext { get; set; }
        public int? width { get; set; }
        public int? height { get; set; }
        public double? tbr { get; set; }      // общий битрейт
        public double? fps { get; set; }
        public string format_note { get; set; }
        public string container { get; set; }
        public string vcodec { get; set; }
        public string acodec { get; set; }
        public string protocol { get; set; }
        public string audio_ext { get; set; }  // "none" если нет
        public string video_ext { get; set; }  // "none" если нет
        public string resolution { get; set; } // строка, иногда вида "1280x720" или "audio only"
        public string url { get; set; }
        public string format { get; set; }     // человекочитаемая строка
    }

    public class Subtitle
    {
        public string ext { get; set; }
        public string url { get; set; }
    }

    // Упрощённый VM для вывода в комбобокс
    public class FormatDisplay
    {
        public Format Model { get; set; }
        public string Display { get; set; }
    }
}
