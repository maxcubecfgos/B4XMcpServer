using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using B4XMcpServer.Engine;
using B4XMcpServer.Repositories;
using B4XMcpServer.Utils;
using ModelContextProtocol.Server;

namespace B4XMcpServer.Tools.Layout
{
    [McpServerToolType]
    public sealed class LayoutControlTools
    {
        private readonly IFileRepository _fileRepository;

        public LayoutControlTools(IFileRepository fileRepository)
        {
            _fileRepository = fileRepository;
        }

        [McpServerTool, Description("Lists all controls in a layout file with their name, type, position, size, and children hierarchy. Use this to understand the structure before adding, removing, or moving controls.")]
        public string ListLayoutControls(
            [Description("Absolute path to the .bal or .bil layout file")] string layoutPath)
        {
            PathSecurity.ValidateAbsolutePath(layoutPath, nameof(layoutPath));

            if (!_fileRepository.Exists(layoutPath))
                throw new FileNotFoundException($"Layout file not found: {layoutPath}");

            var layout = LayoutHelpers.LoadLayout(_fileRepository, layoutPath);
            var controls = new List<object>();
            FlattenControls(layout.RootControl, "", controls);

            return JsonSerializer.Serialize(new
            {
                file = layoutPath,
                controlCount = controls.Count,
                controls
            }, JsonOptions.Default);
        }

        [McpServerTool, Description("Adds a new control to an existing layout file. Valid control types depend on the platform (B4A/B4J): Panel/Pane, Label, Button, TextField/EditText, ImageView, ScrollView, WebView, Switch, Slider, ProgressView, CheckBox (B4A), RadioButton (B4A), Spinner (B4A), ComboBox (B4J), TextView/TextArea (B4J). Creates .bak backup first.")]
        public string LayoutAddControl(
            [Description("Absolute path to the .bal or .bil layout file")] string layoutPath,
            [Description("Control type, e.g. 'Button', 'Label', 'Panel', 'TextField', 'ImageView'.")] string controlType,
            [Description("Optional: control name. Auto-generated if omitted (e.g. 'Button1').")] string? controlName = null,
            [Description("Optional: parent control name. If omitted, adds to root.")] string? parentName = null,
            [Description("Optional X position. Default 10.")] int x = 10,
            [Description("Optional Y position. Default 10.")] int y = 10,
            [Description("Optional width. Default 100.")] int width = 100,
            [Description("Optional height. Default 50.")] int height = 50)
        {
            PathSecurity.ValidateAbsolutePath(layoutPath, nameof(layoutPath));

            if (!_fileRepository.Exists(layoutPath))
                throw new FileNotFoundException($"Layout file not found: {layoutPath}");

            var layout = LayoutHelpers.LoadLayout(_fileRepository, layoutPath);
            var platform = LayoutHelpers.GetPlatform(layoutPath);

            var displayName = ResolveControlType(controlType, platform);
            if (displayName == null)
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Unknown or unsupported control type '{controlType}' for platform {platform}.",
                    supportedTypes = ControlRegistry.GetControlTypesForPlatform(platform)
                }, JsonOptions.Default);

            ControlNode parent;
            if (string.IsNullOrEmpty(parentName))
            {
                parent = layout.RootControl;
            }
            else
            {
                parent = LayoutHelpers.FindControl(layout, parentName)
                    ?? throw new InvalidOperationException($"Parent control '{parentName}' not found in layout.");
            }

            var existingNames = LayoutHelpers.CollectControlNames(layout);

            if (!string.IsNullOrEmpty(controlName))
            {
                if (existingNames.Contains(controlName))
                    return JsonSerializer.Serialize(new { success = false, error = $"A control named '{controlName}' already exists." }, JsonOptions.Default);
            }

            var control = ControlRegistry.CreateControl(displayName, platform, existingNames, x, y,
                layout.Variants.Count, sourceVariantIndex: 0, layout.GridSize);

            if (control == null)
                return JsonSerializer.Serialize(new { success = false, error = $"Failed to create control of type '{controlType}'." }, JsonOptions.Default);

            var generatedName = PropertyModel.GetStr(control, "name", "");
            var finalName = !string.IsNullOrEmpty(controlName) ? controlName : generatedName;

            control.Properties["name"] = new StringRefValue(finalName);
            control.Properties["eventName"] = new StringRefValue(finalName);
            control.Properties["parent"] = new StringRefValue(parentName ?? "");

            parent.Children.Add(control);

            var backup = LayoutHelpers.SaveLayoutWithBackup(_fileRepository, layoutPath, layout);
            var pos = LayoutHelpers.ReadVariant(control, 0);

            return JsonSerializer.Serialize(new
            {
                success = true,
                path = layoutPath,
                backup,
                controlAdded = new
                {
                    name = finalName,
                    type = displayName,
                    position = $"{pos.Left}, {pos.Top}",
                    size = $"{pos.Width}x{pos.Height}"
                }
            }, JsonOptions.Default);
        }

        [McpServerTool, Description("Removes one or more controls from a layout by name. Names can be a single string or comma-separated list. Creates .bak backup first.")]
        public string LayoutRemoveControl(
            [Description("Absolute path to the .bal or .bil layout file")] string layoutPath,
            [Description("Control name(s) to remove. Single name or comma-separated list.")] string controlNames)
        {
            PathSecurity.ValidateAbsolutePath(layoutPath, nameof(layoutPath));

            if (!_fileRepository.Exists(layoutPath))
                throw new FileNotFoundException($"Layout file not found: {layoutPath}");

            var names = controlNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (names.Length == 0)
                throw new ArgumentException("No control names provided.");

            var layout = LayoutHelpers.LoadLayout(_fileRepository, layoutPath);
            var removed = new List<string>();
            var notFound = new List<string>();

            foreach (var name in names)
            {
                if (RemoveControl(layout, name))
                    removed.Add(name);
                else
                    notFound.Add(name);
            }

            if (removed.Count == 0)
                return JsonSerializer.Serialize(new { success = false, error = "No controls were removed.", notFound }, JsonOptions.Default);

            var backup = LayoutHelpers.SaveLayoutWithBackup(_fileRepository, layoutPath, layout);

            return JsonSerializer.Serialize(new
            {
                success = true,
                path = layoutPath,
                backup,
                removed,
                notFound = notFound.Count > 0 ? notFound : null
            }, JsonOptions.Default);
        }

        [McpServerTool, Description("Moves and/or resizes a control in a layout. Only specify the properties you want to change — omitted values keep their current setting. Creates .bak backup first.")]
        public string LayoutMoveControl(
            [Description("Absolute path to the .bal or .bil layout file")] string layoutPath,
            [Description("Control name to modify")] string controlName,
            [Description("New X position (omit to keep current)")] int? left = null,
            [Description("New Y position (omit to keep current)")] int? top = null,
            [Description("New width (omit to keep current)")] int? width = null,
            [Description("New height (omit to keep current)")] int? height = null)
        {
            PathSecurity.ValidateAbsolutePath(layoutPath, nameof(layoutPath));

            if (!_fileRepository.Exists(layoutPath))
                throw new FileNotFoundException($"Layout file not found: {layoutPath}");

            if (left == null && top == null && width == null && height == null)
                throw new ArgumentException("At least one of left, top, width, or height must be specified.");

            var layout = LayoutHelpers.LoadLayout(_fileRepository, layoutPath);
            var control = LayoutHelpers.FindControl(layout, controlName)
                ?? throw new InvalidOperationException($"Control '{controlName}' not found in layout.");

            LayoutHelpers.SetVariant(control, 0, left, top, width, height);

            var backup = LayoutHelpers.SaveLayoutWithBackup(_fileRepository, layoutPath, layout);
            var pos = LayoutHelpers.ReadVariant(control, 0);

            return JsonSerializer.Serialize(new
            {
                success = true,
                path = layoutPath,
                backup,
                control = controlName,
                newPosition = (left.HasValue || top.HasValue) ? $"{pos.Left},{pos.Top}" : null,
                newSize = (width.HasValue || height.HasValue) ? $"{pos.Width}x{pos.Height}" : null
            }, JsonOptions.Default);
        }

        [McpServerTool, Description("Modifies a specific property of a control in a layout. For variant-dependent properties (left, top, width, height, hanchor, vanchor), the value is applied to variant 0. Creates .bak backup first.")]
        public string LayoutModifyProperty(
            [Description("Absolute path to the .bal or .bil layout file")] string layoutPath,
            [Description("Control name to modify")] string controlName,
            [Description("Property name, e.g. 'text', 'enabled', 'textColor', 'left', 'width'.")] string propertyName,
            [Description("Property type: StringRef, String, Int32, Float, Double, Bool, or Color.")] string propertyType,
            [Description("New value as a string. For Color, use #AARRGGBB or #RRGGBB hex notation.")] string value)
        {
            PathSecurity.ValidateAbsolutePath(layoutPath, nameof(layoutPath));

            if (!_fileRepository.Exists(layoutPath))
                throw new FileNotFoundException($"Layout file not found: {layoutPath}");

            var layout = LayoutHelpers.LoadLayout(_fileRepository, layoutPath);
            var control = LayoutHelpers.FindControl(layout, controlName)
                ?? throw new InvalidOperationException($"Control '{controlName}' not found in layout.");

            if (IsVariantProperty(propertyName))
            {
                ApplyVariantProperty(control, propertyName, propertyType, value);
            }
            else
            {
                control.Properties[propertyName] = ParsePropertyValue(propertyType, value)
                    ?? throw new InvalidOperationException($"Unable to parse value '{value}' as type '{propertyType}'.");
            }

            var backup = LayoutHelpers.SaveLayoutWithBackup(_fileRepository, layoutPath, layout);

            return JsonSerializer.Serialize(new
            {
                success = true,
                path = layoutPath,
                backup,
                control = controlName,
                property = propertyName,
                value
            }, JsonOptions.Default);
        }

        [McpServerTool, Description("Moves a control to a new parent node. The control is removed from its current parent and appended to the new parent's children. Creates .bak backup first.")]
        public string LayoutReparentControl(
            [Description("Absolute path to the .bal or .bil layout file")] string layoutPath,
            [Description("Control name to move")] string controlName,
            [Description("Name of the new parent control")] string newParentName)
        {
            PathSecurity.ValidateAbsolutePath(layoutPath, nameof(layoutPath));

            if (!_fileRepository.Exists(layoutPath))
                throw new FileNotFoundException($"Layout file not found: {layoutPath}");

            var layout = LayoutHelpers.LoadLayout(_fileRepository, layoutPath);

            var control = LayoutHelpers.FindControl(layout, controlName)
                ?? throw new InvalidOperationException($"Control '{controlName}' not found in layout.");

            if (string.Equals(newParentName, "root", StringComparison.OrdinalIgnoreCase))
            {
                // Move to root
                var currentParent = LayoutHelpers.FindParent(layout, controlName);
                if (currentParent == null)
                    return JsonSerializer.Serialize(new { success = false, error = $"Could not determine the current parent of '{controlName}'." }, JsonOptions.Default);

                currentParent.Children.Remove(control);
                layout.RootControl.Children.Add(control);
                control.Properties["parent"] = new StringRefValue("");
            }
            else
            {
                var newParent = LayoutHelpers.FindControl(layout, newParentName)
                    ?? throw new InvalidOperationException($"New parent control '{newParentName}' not found in layout.");

                if (ReferenceEquals(control, newParent) || LayoutHelpers.IsDescendant(control, newParent))
                    return JsonSerializer.Serialize(new { success = false, error = "Cannot reparent a control under itself or one of its descendants." }, JsonOptions.Default);

                var currentParent = LayoutHelpers.FindParent(layout, controlName);
                if (currentParent == null)
                    return JsonSerializer.Serialize(new { success = false, error = $"Could not determine the current parent of '{controlName}'." }, JsonOptions.Default);

                currentParent.Children.Remove(control);
                newParent.Children.Add(control);
                control.Properties["parent"] = new StringRefValue(newParentName);
            }

            var backup = LayoutHelpers.SaveLayoutWithBackup(_fileRepository, layoutPath, layout);

            return JsonSerializer.Serialize(new
            {
                success = true,
                path = layoutPath,
                backup,
                control = controlName,
                newParent = newParentName
            }, JsonOptions.Default);
        }

        private static void FlattenControls(ControlNode node, string parentPath, List<object> result)
        {
            var name = PropertyModel.GetStr(node, "name", "") ?? PropertyModel.GetStr(node, "eventName", "");
            var javaType = PropertyModel.GetStr(node, "javaType", "");
            var csType = PropertyModel.GetStr(node, "csType", "");
            var type = LayoutHelpers.GetShortTypeName(javaType);
            var pos = LayoutHelpers.ReadVariant(node, 0);
            var text = PropertyModel.GetStr(node, "text", "") ?? PropertyModel.GetStr(node, "hintText", "");

            result.Add(new
            {
                name = string.IsNullOrEmpty(name) ? "(root)" : name,
                type,
                javaType,
                csType,
                position = $"{pos.Left}, {pos.Top}",
                size = $"{pos.Width}x{pos.Height}",
                text = string.IsNullOrEmpty(text) ? null : text,
                parentPath = string.IsNullOrEmpty(parentPath) ? null : parentPath,
                childCount = node.Children.Count
            });

            var currentPath = string.IsNullOrEmpty(parentPath) ? name : $"{parentPath} > {name}";
            foreach (var child in node.Children)
                FlattenControls(child, currentPath, result);
        }

        private static bool RemoveControl(LayoutFile layout, string name)
        {
            var parent = LayoutHelpers.FindParent(layout, name);
            if (parent == null) return false;

            var control = parent.Children.FirstOrDefault(c =>
            {
                var n = PropertyModel.GetStr(c, "name", "") ?? PropertyModel.GetStr(c, "eventName", "");
                return string.Equals(n, name, StringComparison.OrdinalIgnoreCase);
            });

            if (control == null) return false;
            parent.Children.Remove(control);
            return true;
        }

        private static string? ResolveControlType(string input, Platform platform)
        {
            var available = ControlRegistry.GetControlTypesForPlatform(platform);
            return available.FirstOrDefault(t => string.Equals(t, input, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsVariantProperty(string propertyName)
        {
            return propertyName is "left" or "top" or "width" or "height" or "hanchor" or "vanchor";
        }

        private static void ApplyVariantProperty(ControlNode control, string propertyName, string propertyType, string value)
        {
            var intValue = int.TryParse(value, out var i) ? i : (int?)null;
            if (!intValue.HasValue)
                throw new InvalidOperationException($"Variant property '{propertyName}' requires an integer value.");

            switch (propertyName)
            {
                case "left": LayoutHelpers.SetVariant(control, 0, left: intValue.Value); break;
                case "top": LayoutHelpers.SetVariant(control, 0, top: intValue.Value); break;
                case "width": LayoutHelpers.SetVariant(control, 0, width: intValue.Value); break;
                case "height": LayoutHelpers.SetVariant(control, 0, height: intValue.Value); break;
                case "hanchor": LayoutHelpers.SetVariant(control, 0, hanchor: intValue.Value); break;
                case "vanchor": LayoutHelpers.SetVariant(control, 0, vanchor: intValue.Value); break;
            }
        }

        private static PropertyValue? ParsePropertyValue(string propertyType, string value)
        {
            return propertyType.ToLowerInvariant() switch
            {
                "stringref" => new StringRefValue(value),
                "string" => new StringValue(value),
                "int32" or "int" => int.TryParse(value, out var i) ? new IntValue(i) : null,
                "float" => float.TryParse(value, out var f) ? new FloatValue(f) : null,
                "double" => double.TryParse(value, out var d) ? new DoubleValue(d) : null,
                "bool" or "boolean" => bool.TryParse(value, out var b) ? new BoolValue(b) : null,
                "color" => ParseColorValue(value),
                _ => null
            };
        }

        private static ColorValue? ParseColorValue(string value)
        {
            var hex = value.Trim();
            if (hex.StartsWith("#")) hex = hex.Substring(1);
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex.Substring(2);

            if (hex.Length == 6)
            {
                var r = Convert.ToByte(hex.Substring(0, 2), 16);
                var g = Convert.ToByte(hex.Substring(2, 2), 16);
                var b = Convert.ToByte(hex.Substring(4, 2), 16);
                return new ColorValue(255, r, g, b);
            }

            if (hex.Length == 8)
            {
                var a = Convert.ToByte(hex.Substring(0, 2), 16);
                var r = Convert.ToByte(hex.Substring(2, 2), 16);
                var g = Convert.ToByte(hex.Substring(4, 2), 16);
                var b = Convert.ToByte(hex.Substring(6, 2), 16);
                return new ColorValue(a, r, g, b);
            }

            return null;
        }
    }
}
