using System;
using System.Web.Script.Serialization;

#if USE_SYSTEM_TEXT_JSON
using System.Text.Json;
#endif

namespace YTDlpGui.Services
{
    public interface IJsonParser
    {
        T Deserialize<T>(string json);
    }

    // Встроенный в .NET Framework способ — без внешних библиотек
    public class JavaScriptSerializerParser : IJsonParser
    {
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue,
