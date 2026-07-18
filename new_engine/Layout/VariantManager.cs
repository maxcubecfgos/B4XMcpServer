using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace B4XEngineCore
{
    public static class VariantManager
    {
        public record PredefinedLayout(string Label, int Width, int Height, double Scale);

        private static readonly List<PredefinedLayout> B4ALayouts = new()
        {
            new("Phone (portrait)", 320, 480, 1),
            new("Phone (landscape)", 480, 320, 1),
            new("7'' Tablet (portrait)", 600, 960, 1),
            new("7'' Tablet (landscape)", 960, 600, 1),
            new("10'' Tablet (portrait)", 800, 1280, 1),
            new("10'' Tablet (landscape)", 1280, 800, 1),
            new("Nexus One (portrait)", 480, 800, 1.5),
            new("Nexus One (landscape)", 800, 480, 1.5),
            new("Nexus 5 (portrait)", 1080, 1920, 3),
            new("Nexus 5 (landscape)", 1920, 1080, 3),
        };

        private static readonly List<PredefinedLayout> B4JLayouts = new()
        {
            new("Small Window", 400, 400, 1),
            new("Default Window", 600, 600, 1),
            new("Large Window", 800, 600, 1),
            new("Wide Window", 1024, 768, 1),
            new("Full HD", 1920, 1080, 1),
        };

        public static List<PredefinedLayout> GetPredefinedLayouts(Platform platform)
            => platform == Platform.B4J ? B4JLayouts : B4ALayouts;

        public static List<PredefinedLayout> ParseAbstractLayouts(string text)
        {
            var result = new List<PredefinedLayout>();
            var regex = new Regex(@"([^:]*):\s*(\d+)x(\d+),\s*scale\s*=\s*([\d.]+)");
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed == "-") continue;
                var match = regex.Match(trimmed);
                if (!match.Success) continue;
                result.Add(new PredefinedLayout(match.Groups[1].Value.Trim(),
                    int.Parse(match.Groups[2].Value),
                    int.Parse(match.Groups[3].Value),
                    double.Parse(match.Groups[4].Value)));
            }
            return result;
        }

        public static string FormatVariant(Variant v) => $"{v.Width}x{v.Height}, scale={v.Scale}";

        public static int AddVariant(List<Variant> variants, ControlNode rootControl, Variant newVariant, int currentVariantIndex)
        {
            if (variants.Any(v => v.Width == newVariant.Width && v.Height == newVariant.Height && Math.Abs(v.Scale - newVariant.Scale) < 0.001))
                return -1;

            int newIndex = variants.Count;
            variants.Add(newVariant);
            AddVariantToControlTree(rootControl, newIndex, currentVariantIndex);
            return newIndex;
        }

        public static bool RemoveVariant(List<Variant> variants, ControlNode rootControl, int variantIndex)
        {
            if (variants.Count <= 1 || variantIndex < 0 || variantIndex >= variants.Count) return false;
            variants.RemoveAt(variantIndex);
            RemoveVariantFromControlTree(rootControl, variantIndex);
            return true;
        }

        public static Variant GetDefaultVariant(Platform platform)
            => platform == Platform.B4J ? new(1, 600, 600) : new(1, 320, 480);

        public static int FindClosestVariant(List<Variant> variants, int targetWidth, int targetHeight, double targetScale)
        {
            if (variants.Count <= 1) return 0;
            int bestIndex = 0;
            double bestDiff = double.PositiveInfinity;

            for (int i = 0; i < variants.Count; i++)
            {
                var v = variants[i];
                double vW = v.Width / v.Scale, vH = v.Height / v.Scale;
                double tW = targetWidth / targetScale, tH = targetHeight / targetScale;
                double areaDiff = Math.Abs(vW * vH - tW * tH);
                double aspectDiff = Math.Abs(vW / vH - tW / tH) * 10000;
                double diff = areaDiff + aspectDiff;
                if (diff < bestDiff) { bestDiff = diff; bestIndex = i; }
            }
            return bestIndex;
        }

        public static int CountControlVariants(ControlNode node)
            => node.Properties.Keys.Count(k => Regex.IsMatch(k, @"^variant\d+$"));

        private static void AddVariantToControlTree(ControlNode node, int newVariantIndex, int sourceVariantIndex)
        {
            string sourceKey = $"variant{sourceVariantIndex}";
            string newKey = $"variant{newVariantIndex}";

            if (node.Properties.TryGetValue(sourceKey, out var so) && so is ObjectValue sov)
            {
                var newMap = sov.Value.ToDictionary(k => k.Key, k => CloneVariantPropertyValue(k.Value));
                node.Properties[newKey] = new ObjectValue(newMap);
            }
            else
            {
                node.Properties[newKey] = new ObjectValue(new()
                {
                    ["left"] = new IntValue(ExtractNumber(node.Properties.GetValueOrDefault("left"), 0)),
                    ["top"] = new IntValue(ExtractNumber(node.Properties.GetValueOrDefault("top"), 0)),
                    ["width"] = new IntValue(ExtractNumber(node.Properties.GetValueOrDefault("width"), 100)),
                    ["height"] = new IntValue(ExtractNumber(node.Properties.GetValueOrDefault("height"), 50)),
                });
            }

            foreach (var child in node.Children)
                AddVariantToControlTree(child, newVariantIndex, sourceVariantIndex);
        }

        private static void RemoveVariantFromControlTree(ControlNode node, int removedIndex)
        {
            var variantKeys = node.Properties.Keys.Where(k => Regex.IsMatch(k, @"^variant(\d+)$"))
                .Select(k => int.Parse(Regex.Match(k, @"^variant(\d+)$").Groups[1].Value))
                .OrderBy(i => i).ToList();

            node.Properties.Remove($"variant{removedIndex}");
            foreach (int idx in variantKeys.Where(i => i > removedIndex))
            {
                if (node.Properties.TryGetValue($"variant{idx}", out var val))
                {
                    node.Properties.Remove($"variant{idx}");
                    node.Properties[$"variant{idx - 1}"] = val;
                }
            }

            foreach (var child in node.Children)
                RemoveVariantFromControlTree(child, removedIndex);
        }

        private static PropertyValue CloneVariantPropertyValue(PropertyValue v)
        {
            if (v is ObjectValue ov)
                return new ObjectValue(ov.Value.ToDictionary(k => k.Key, k => CloneVariantPropertyValue(k.Value)));
            return v;
        }

        private static int ExtractNumber(PropertyValue? v, int def)
        {
            return v switch
            {
                IntValue iv => iv.Value,
                FloatValue fv => (int)fv.Value,
                DoubleValue dv => (int)dv.Value,
                _ => def,
            };
        }
    }
}
