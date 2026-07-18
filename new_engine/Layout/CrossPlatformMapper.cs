using System;
using System.Collections.Generic;
using System.IO;

namespace B4XEngineCore
{
    public static class CrossPlatformMapper
    {
        private static readonly Dictionary<string, (string B4I, string B4J)> Mappings = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Button"] = ("Button", "Button"),
            ["Label"] = ("Label", "Label"),
            ["EditText"] = ("TextField", "TextField"),
            ["Panel"] = ("Panel", "Pane"),
            ["ScrollView"] = ("ScrollView", "ScrollPane"),
            ["ImageView"] = ("ImageView", "ImageView"),
            ["ListView"] = ("TableView", "ListView"),
            ["WebView"] = ("WebView", "WebView"),
            ["CheckBox"] = ("Switch", "CheckBox"),
            ["RadioButton"] = ("SegmentedControl", "RadioButton"),
            ["Spinner"] = ("Picker", "ComboBox"),
            ["SeekBar"] = ("Slider", "Slider"),
            ["ProgressBar"] = ("ProgressView", "ProgressBar"),
            ["DatePicker"] = ("DatePicker", "DatePicker"),
            ["MapView"] = ("MapView", "MapView"),
            ["Camera"] = ("Camera", "Camera"),
            ["VideoView"] = ("VideoView", "VideoView"),
            ["ActivityIndicator"] = ("ActivityIndicator", "ActivityIndicator"),
            ["Stepper"] = ("Stepper", "Spinner"),
            ["SegmentedControl"] = ("SegmentedControl", "ToggleButton"),
            ["CalendarView"] = ("CalendarPicker", "CalendarView"),
            ["ViewPager"] = ("PageControl", "TabPane"),
        };

        private static readonly Dictionary<string, string> B4IToB4J = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> B4JToB4I = new(StringComparer.OrdinalIgnoreCase);

        static CrossPlatformMapper()
        {
            foreach (var kvp in Mappings)
            {
                B4IToB4J[kvp.Value.B4I] = kvp.Value.B4J;
                B4JToB4I[kvp.Value.B4J] = kvp.Value.B4I;
            }
        }

        public static string MapB4AToB4J(string b4aControlType)
            => Mappings.TryGetValue(b4aControlType, out var m) ? m.B4J : b4aControlType;

        public static string MapB4AToB4I(string b4aControlType)
            => Mappings.TryGetValue(b4aControlType, out var m) ? m.B4I : b4aControlType;

        public static string MapB4IToB4J(string b4iControlType)
            => B4IToB4J.TryGetValue(b4iControlType, out var m) ? m : b4iControlType;

        public static string MapB4JToB4I(string b4jControlType)
            => B4JToB4I.TryGetValue(b4jControlType, out var m) ? m : b4jControlType;

        public static string? Resolve(string sourceType, Platform fromPlatform, Platform toPlatform)
        {
            if (fromPlatform == toPlatform) return sourceType;
            if (fromPlatform == Platform.B4A && toPlatform == Platform.B4J) return MapB4AToB4J(sourceType);
            if (fromPlatform == Platform.B4A) return MapB4AToB4I(sourceType);
            if (fromPlatform == Platform.B4J && toPlatform == Platform.B4A)
            {
                foreach (var kvp in Mappings)
                    if (kvp.Value.B4J.Equals(sourceType, StringComparison.OrdinalIgnoreCase))
                        return kvp.Key;
            }
            if (fromPlatform == Platform.B4J)
            {
                foreach (var kvp in Mappings)
                    if (kvp.Value.B4J.Equals(sourceType, StringComparison.OrdinalIgnoreCase))
                        return kvp.Key;
            }
            return null;
        }

        public static bool TryParseCsv(StreamReader csvReader)
        {
            try
            {
                string? header = csvReader.ReadLine();
                if (header == null) return false;
                while (!csvReader.EndOfStream)
                {
                    string? line = csvReader.ReadLine();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var parts = line.Split(',');
                    if (parts.Length < 3) continue;
                    string b4a = parts[0].Trim('"');
                    string b4i = parts[1].Trim('"');
                    string b4j = parts[2].Trim('"');
                    Mappings[b4a] = (b4i, b4j);
                }
                return true;
            }
            catch { return false; }
        }
    }
}
