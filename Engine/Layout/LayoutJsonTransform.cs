using System;
using System.Text.Json;

namespace B4XMcpServer.Engine
{
    public static class LayoutJsonTransform
    {
        public static string LayoutToJson(byte[] layoutData)
        {
            var layout = LayoutParser.ParseLayoutFile(layoutData);
            var json = new JsonSerializerOptions
            {
                WriteIndented = true,
                IncludeFields = true,
            };
            return JsonSerializer.Serialize(layout, json);
        }

        public static byte[] JsonToLayout(string json)
        {
            var options = new JsonSerializerOptions { IncludeFields = true, PropertyNameCaseInsensitive = true };
            var layout = JsonSerializer.Deserialize<LayoutFile>(json, options)
                ?? throw new InvalidOperationException("Failed to deserialize layout JSON");
            return LayoutWriter.WriteLayoutFile(layout);
        }
    }
}
