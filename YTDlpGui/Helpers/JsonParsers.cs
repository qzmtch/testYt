using System;
using System.Web.Script.Serialization;
using YtDlpGui.Models;

// Если захотите использовать System.Text.Json, добавьте пакет System.Text.Json (net472 поддерживается)
// и раскомментируйте USING_SYSTEM_TEXT_JSON + код ниже.
//#define USING_SYSTEM_TEXT_JSON

#if USING_SYSTEM_TEXT_JSON
using System.Text.Json;
using System.Text.Json.Serialization;
#endif

namespace YtDlpGui.Helpers
{
    public static class JsonParsers
    {
        public static YtDlpInfo ParseWithJavaScriptSerializer(string json)
        {
            var serializer = new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue,
                RecursionLimit = 100
            };
            return serializer.Deserialize<YtDlpInfo>(json);
        }

#if USING_SYSTEM_TEXT_JSON
        public static YtDlpInfo ParseWithSystemTextJson(string json)
        {
            var opts 
