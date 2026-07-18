using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using B4XEngineCore;
using B4XMcpServer.Repositories;

namespace B4XMcpServer.Tools.Layout
{
    /// <summary>
    /// Shared helpers for the layout tool classes. All layout I/O goes through
    /// <see cref="B4XEngineCore.LayoutParser"/> and <see cref="B4XEngineCore.LayoutWriter"/>
    /// so the tools never re-implement the binary format themselves.
    /// </summary>
    public static class LayoutHelpers
    {
        public static LayoutFile LoadLayout(IFileRepository fileRepo, string layoutPath)
        {
            var data = fileRepo.ReadBytes(layoutPath);
            return LayoutParser.ParseLayoutFile(data);
        }

        public static void SaveLayout(IFileRepository fileRepo, string layoutPath, LayoutFile layout)
        {
            RebuildManifest(layout);
            var data = LayoutWriter.WriteLayoutFile(layout);
            fileRepo.WriteBytes(layoutPath, data);
        }

        public static string? SaveLayoutWithBackup(IFileRepository fileRepo, string layoutPath, LayoutFile layout)
        {
            var backup = fileRepo.BackupPath(layoutPath);
            SaveLayout(fileRepo, layoutPath, layout);
            return backup;
        }

        public static void RebuildManifest(LayoutFile layout)
        {
            layout.Manifest = ControlRegistry.CollectManifestEntries(layout.RootControl)
                .Select(e => new ManifestEntry(e.Name, e.JavaType, e.CsType))
                .ToList();
        }

        public static Platform GetPlatform(string layoutPath)
        {
            return PlatformDetector.DetectPlatform(layoutPath);
        }

        public static ControlNode? FindControl(LayoutFile layout, string name)
        {
            return FindControlRecursive(layout.RootControl, name);
        }

        private static ControlNode? FindControlRecursive(ControlNode node, string name)
        {
            var nodeName = PropertyModel.GetStr(node, "name", "") ?? PropertyModel.GetStr(node, "eventName", "");
            if (string.Equals(nodeName, name, StringComparison.OrdinalIgnoreCase))
                return node;

            foreach (var child in node.Children)
            {
                var found = FindControlRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }

        public static ControlNode? FindParent(LayoutFile layout, string childName)
        {
            return FindParentRecursive(layout.RootControl, childName);
        }

        private static ControlNode? FindParentRecursive(ControlNode parent, string childName)
        {
            foreach (var child in parent.Children)
            {
                var name = PropertyModel.GetStr(child, "name", "") ?? PropertyModel.GetStr(child, "eventName", "");
                if (string.Equals(name, childName, StringComparison.OrdinalIgnoreCase))
                    return parent;

                var found = FindParentRecursive(child, childName);
                if (found != null) return found;
            }
            return null;
        }

        public static HashSet<string> CollectControlNames(LayoutFile layout)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectControlNamesRecursive(layout.RootControl, names);
            return names;
        }

        private static void CollectControlNamesRecursive(ControlNode node, HashSet<string> names)
        {
            var name = PropertyModel.GetStr(node, "name", "") ?? PropertyModel.GetStr(node, "eventName", "");
            if (!string.IsNullOrEmpty(name))
                names.Add(name);

            foreach (var child in node.Children)
                CollectControlNamesRecursive(child, names);
        }

        public static bool IsDescendant(ControlNode ancestor, ControlNode candidate)
        {
            foreach (var child in ancestor.Children)
            {
                if (ReferenceEquals(child, candidate)) return true;
                if (IsDescendant(child, candidate)) return true;
            }
            return false;
        }

        public static (int Left, int Top, int Width, int Height) ReadVariant(ControlNode node, int variantIndex)
        {
            return (
                (int)PropertyModel.ReadVariantProperty(node, "left", variantIndex),
                (int)PropertyModel.ReadVariantProperty(node, "top", variantIndex),
                (int)PropertyModel.ReadVariantProperty(node, "width", variantIndex),
                (int)PropertyModel.ReadVariantProperty(node, "height", variantIndex)
            );
        }

        public static void SetVariant(ControlNode node, int variantIndex,
            int? left = null, int? top = null, int? width = null, int? height = null,
            int? hanchor = null, int? vanchor = null)
        {
            var variantKey = $"variant{variantIndex}";
            if (!node.Properties.TryGetValue(variantKey, out var variantProp) || variantProp is not ObjectValue variantObj)
            {
                variantObj = new ObjectValue(new Dictionary<string, PropertyValue>());
                node.Properties[variantKey] = variantObj;
            }

            if (left.HasValue) variantObj.Value["left"] = new IntValue(left.Value);
            if (top.HasValue) variantObj.Value["top"] = new IntValue(top.Value);
            if (width.HasValue) variantObj.Value["width"] = new IntValue(width.Value);
            if (height.HasValue) variantObj.Value["height"] = new IntValue(height.Value);
            if (hanchor.HasValue) variantObj.Value["hanchor"] = new IntValue(hanchor.Value);
            if (vanchor.HasValue) variantObj.Value["vanchor"] = new IntValue(vanchor.Value);
        }

        public static string GetShortTypeName(string javaType)
        {
            if (string.IsNullOrEmpty(javaType)) return "B4XView";

            var lowered = javaType.TrimStart('.').ToLowerInvariant();
            if (lowered.Contains("buttonwrapper")) return "Button";
            if (lowered.Contains("labelwrapper")) return "Label";
            if (lowered.Contains("edittextwrapper") || lowered.Contains("textfieldwrapper")) return "EditText";
            if (lowered.Contains("panelwrapper") || lowered.Contains("panewrapper")) return "Panel";
            if (lowered.Contains("checkboxwrapper")) return "CheckBox";
            if (lowered.Contains("imageviewwrapper")) return "ImageView";
            if (lowered.Contains("scrollviewwrapper")) return "ScrollView";
            if (lowered.Contains("webviewwrapper")) return "WebView";
            if (lowered.Contains("seekbarwrapper") || lowered.Contains("sliderwrapper")) return "Slider";
            if (lowered.Contains("spinnerwrapper") || lowered.Contains("comboboxwrapper")) return "Spinner";
            if (lowered.Contains("progressbarwrapper") || lowered.Contains("progressviewwrapper")) return "ProgressBar";
            if (lowered.Contains("datepickerwrapper")) return "DatePicker";
            if (lowered.Contains("activitywrapper")) return "Activity";

            return "B4XView";
        }

        public static string EnsureLayoutDirectory(string layoutPath)
        {
            var dir = Path.GetDirectoryName(layoutPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            return dir ?? "";
        }
    }
}
