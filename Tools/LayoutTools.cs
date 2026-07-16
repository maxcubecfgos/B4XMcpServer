using B4XMcpServer.Engine;
using B4XMcpServer.Services;
using B4XMcpServer.Utils;
using ModelContextProtocol.Server;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace B4XMcpServer.Tools
{
    [McpServerToolType]
    public sealed class LayoutTools
    {
        // Regex timeout protects against catastrophic backtracking on untrusted input.
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

        // ── Write Layout ──────────────────────────────────────────────

        [McpServerTool, Description("Writes a B4X layout file (.bal or .bil) from JSON. Validates structure before writing. Creates a .bak backup before overwriting if the file exists. If the file doesn't exist, it will be created.")]
        public static string WriteLayout(
            [Description("Absolute path to the .bal or .bil layout file to write")] string layoutPath,
            [Description("Layout JSON. Must contain: version, gridSize, variants, manifest, fileReferences, scriptData, flags, rootControl.")] string jsonContent)
        {
            PathSecurity.ValidateAbsolutePath(layoutPath, nameof(layoutPath));

            // For destructive writes, keep them inside the project root.
            string? projectRoot = ProjectScanner.FindProjectRoot(PathSecurity.GetDirectoryForProjectRoot(layoutPath));
            if (projectRoot != null)
                PathSecurity.ValidateWithinBaseDirectory(layoutPath, projectRoot, nameof(layoutPath));

            var ext = Path.GetExtension(layoutPath).ToLowerInvariant();
            if (ext != ".bal" && ext != ".bil")
                throw new ArgumentException("File must have .bal or .bil extension");

            JsonObject? json;
            try { json = JsonNode.Parse(jsonContent)?.AsObject(); }
            catch (Exception ex) { throw new FormatException($"Invalid JSON: {ex.Message}", ex); }
            if (json == null) throw new FormatException("Invalid JSON: root must be an object.");

            var errors = ValidateLayoutJson(json);
            if (errors.Count > 0)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    validationErrors = errors,
                    hint = "Fix the validation errors above and try again. Use create_empty_layout to get a valid starting template."
                }, JsonOptions.Default);
            }

            // Backup only if file exists
            string backupPath = layoutPath + ".bak";
            if (File.Exists(layoutPath))
            {
                File.Copy(layoutPath, backupPath, overwrite: true);
            }

            // Ensure directory exists
            string? dir = Path.GetDirectoryName(layoutPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            byte[] data;
            try
            {
                data = BalEncoder.Encode(jsonContent);
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Failed to encode layout: {ex.Message}",
                    hint = "The JSON structure may be invalid for the binary format. Use create_empty_layout to get a valid template, then modify its properties."
                }, JsonOptions.Default);
            }

            File.WriteAllBytes(layoutPath, data);

            return JsonSerializer.Serialize(new
            {
                success = true,
                path = layoutPath,
                backup = File.Exists(backupPath) ? backupPath : null,
                bytesWritten = data.Length
            }, JsonOptions.Default);
        }

        // ── Create Empty Layout ──────────────────────────────────────

        [McpServerTool, Description("Generates a minimal valid B4X layout JSON that can be used as a starting point for write_layout. Creates an empty layout with the correct root control for the specified platform (B4A or B4J).")]
        public static string CreateEmptyLayout(
            [Description("Target platform: 'b4a' (Android) or 'b4j' (B4J/Desktop).")] string platform = "b4a")
        {
            bool isB4J = platform.ToLowerInvariant() == "b4j";

            var json = new JsonObject
            {
                ["version"] = 5,
                ["gridSize"] = 10,
                ["variants"] = new JsonArray { new JsonObject { ["scale"] = 1.0, ["width"] = 320, ["height"] = 480 } },
                ["manifest"] = new JsonArray(),
                ["fileReferences"] = new JsonArray(),
                ["flags"] = new JsonObject { ["c"] = false, ["d"] = false },
                ["scriptData"] = new JsonObject
                {
                    ["mainScript"] = isB4J ? "'All variants script\n" : "'All variants script\nAutoScaleAll\n",
                    ["variantScripts"] = new JsonArray()
                }
            };

            if (isB4J)
            {
                json["rootControl"] = new JsonObject
                {
                    ["properties"] = new JsonObject
                    {
                        ["csType"] = Prop("StringRef", "Dbasic.Designer.MetaMain"),
                        ["type"] = Prop("StringRef", ".PaneWrapper$ConcretePaneWrapper"),
                        ["javaType"] = Prop("StringRef", ".PaneWrapper$ConcretePaneWrapper"),
                        ["name"] = Prop("StringRef", "Main"),
                        ["eventName"] = Prop("StringRef", "MainForm"),
                        ["title"] = Prop("StringRef", "Form"),
                        ["alpha"] = Prop("Float", 1),
                        ["enabled"] = Prop("Bool", true),
                        ["visible"] = Prop("Bool", true),
                        ["handleResizeEvent"] = Prop("Bool", false),
                        ["orientation"] = Prop("StringRef", "INHERIT"),
                        ["borderWidth"] = Prop("Float", 0),
                        ["cornerRadius"] = Prop("Float", 0),
                        ["variant0"] = VariantProp(0, 0, 200, 200)
                    },
                    ["children"] = new JsonArray()
                };
            }
            else
            {
                json["rootControl"] = new JsonObject
                {
                    ["properties"] = new JsonObject
                    {
                        ["csType"] = Prop("StringRef", "Dbasic.Designer.MetaActivity"),
                        ["type"] = Prop("StringRef", ".ActivityWrapper"),
                        ["javaType"] = Prop("StringRef", ".ActivityWrapper"),
                        ["name"] = Prop("StringRef", "Activity"),
                        ["eventName"] = Prop("StringRef", "Activity"),
                        ["title"] = Prop("StringRef", "Activity"),
                        ["fullScreen"] = Prop("Bool", false),
                        ["includeTitle"] = Prop("Bool", true),
                        ["visible"] = Prop("Bool", true),
                        ["animationDuration"] = Prop("Int32", 400),
                        ["variant0"] = VariantProp(100, 100, 100, 100)
                    },
                    ["children"] = new JsonArray()
                };
            }

            // JsonNode.ToJsonString(options) needs an explicit TypeInfoResolver in .NET 8 —
            // anonymous object Serialize calls work fine without one (the framework's reflection
            // resolver handles the simple types), but JsonNode is polymorphic on the wire output
            // and trips the "must specify a TypeInfoResolver" guard. DefaultJsonTypeInfoResolver
            // lives under System.Text.Json.Serialization.Metadata (not the parent .Serialization).
            return json.ToJsonString(JsonOptions.Default);
        }

        // ── List Controls ────────────────────────────────────────────

        [McpServerTool, Description("Lists all controls in a layout file with their name, type, position, size, and children hierarchy. Use this to understand the structure before adding, removing, or moving controls.")]
        public static string ListLayoutControls(
            [Description("Absolute path to the .bal or .bil layout file")] string layoutPath)
        {
            PathSecurity.ValidateAbsolutePath(layoutPath, nameof(layoutPath));

            if (!File.Exists(layoutPath))
                throw new FileNotFoundException($"Layout file not found: {layoutPath}");

            var data = File.ReadAllBytes(layoutPath);
            var decoded = BalDecoder.Decode(data);
            var json = JsonNode.Parse(decoded)?.AsObject();
            if (json == null)
                throw new FormatException("Decoded layout JSON root is not an object.");

            var controls = new List<object>();
            if (json["rootControl"] is JsonObject root)
                FlattenControls(root, "", controls);

            return JsonSerializer.Serialize(new
            {
                file = layoutPath,
                controlCount = controls.Count,
                controls
            }, JsonOptions.Default);
        }

        private static void FlattenControls(JsonObject node, string parentPath, List<object> result)
        {
            var props = node["properties"] as JsonObject;
            if (props == null) return;

            // Resolve a stable display name for the control. We prefer the explicit "name"
            // property and fall back to "eventName" (some root controls set only that).
            string name = GetPropString(props, "name") ?? GetPropString(props, "eventName") ?? "(unnamed)";
            string type = GetPropString(props, "javaType") ?? GetPropString(props, "csType") ?? "?";
            int left = GetVariantInt(props, "left", 0);
            int top = GetVariantInt(props, "top", 0);
            int width = GetVariantInt(props, "width", 0);
            int height = GetVariantInt(props, "height", 0);
            string text = GetPropString(props, "text") ?? GetPropString(props, "hint") ?? "";

            result.Add(new
            {
                name,
                type = type.TrimStart('.'),
                position = $"{left}, {top}",
                size = $"{width}x{height}",
                text = string.IsNullOrEmpty(text) ? null : text,
                parentPath = string.IsNullOrEmpty(parentPath) ? null : parentPath,
                childCount = (node["children"] as JsonArray)?.Count ?? 0
            });

            if (node["children"] is JsonArray children)
            {
                string currentPath = string.IsNullOrEmpty(parentPath) ? name : $"{parentPath} > {name}";
                foreach (var child in children)
                {
                    if (child is JsonObject childObj)
                        FlattenControls(childObj, currentPath, result);
                }
            }
        }

        // ── Add Control ──────────────────────────────────────────────

        [McpServerTool, Description("Adds a new control to an existing layout file. Valid control types for B4A: Button, Label, EditText, Panel, CheckBox, RadioButton, Spinner, ListView, ImageView, WebView, ScrollView, TabStrip. For B4J: Button, Label, TextField, TextArea, CheckBox, RadioButton, ComboBox, ListView, ImageView, Slider, DatePicker. Creates .bak backup first.")]
        public static string LayoutAddControl(
            [Description("Absolute path to the .bal or .bil layout file")] string layoutPath,
            [Description("Control type: 'Button', 'Label', 'EditText', 'Panel', 'CheckBox', 'ImageView', etc.")] string controlType,
            [Description("Optional: control name. Auto-generated if omitted (e.g. 'Button1', 'Label2').")] string? controlName = null,
            [Description("Optional: parent control name. If omitted, adds to root.")] string? parentName = null,
            [Description("Optional X position. Default 10.")] int x = 10,
            [Description("Optional Y position. Default 10.")] int y = 10,
            [Description("Optional width. Default 100.")] int width = 100,
            [Description("Optional height. Default 50.")] int height = 50)
        {
            PathSecurity.ValidateAbsolutePath(layoutPath, nameof(layoutPath));

            if (!File.Exists(layoutPath))
                throw new FileNotFoundException($"Layout file not found: {layoutPath}");

            var ext = Path.GetExtension(layoutPath).ToLowerInvariant();
            bool isB4J = ext == ".bjl";

            var data = File.ReadAllBytes(layoutPath);
            var decoded = BalDecoder.Decode(data);
            var json = JsonNode.Parse(decoded)?.AsObject();
            if (json == null)
                throw new FormatException("Decoded layout JSON root is not an object.");

            // Get root control
            var rootControl = json["rootControl"] as JsonObject;
            if (rootControl == null)
                throw new InvalidOperationException("Layout has no rootControl");

            // Find the parent node (or default to the root). The `if (parentName)` branch
            // returns early when the lookup fails, so parentNode is statically non-null
            // by the time the rest of the method runs — no `!` suppression needed downstream.
            JsonObject parentNode;
            if (string.IsNullOrEmpty(parentName))
            {
                parentNode = rootControl;
            }
            else
            {
                var found = FindControlByName(rootControl, parentName);
                if (found == null)
                    return JsonSerializer.Serialize(new
                    {
                        success = false,
                        error = $"Parent control '{parentName}' not found in layout."
                    }, JsonOptions.Default);
                parentNode = found;
            }

            // Collect existing names
            var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectControlNames(rootControl, existingNames);

            // Generate name if not provided
            string finalName = controlName ?? GenerateControlName(controlType, existingNames);
            existingNames.Add(finalName);

            // Build the new control based on type
            var controlSpec = GetControlDefaults(controlType, isB4J);
            if (controlSpec == null)
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Unknown control type: '{controlType}'. Use list_layout_controls on an existing layout to see valid types, or try: Button, Label, EditText, Panel, CheckBox, ImageView."
                }, JsonOptions.Default);

            var newControl = new JsonObject
            {
                ["properties"] = new JsonObject
                {
                    ["csType"] = Prop("StringRef", controlSpec.CsType),
                    ["type"] = Prop("StringRef", controlSpec.Type),
                    ["javaType"] = Prop("StringRef", controlSpec.JavaType),
                    ["name"] = Prop("StringRef", finalName),
                    ["eventName"] = Prop("StringRef", finalName),
                    ["parent"] = Prop("StringRef", GetPropString(parentNode["properties"] as JsonObject, "name") ?? ""),
                    ["visible"] = Prop("Bool", true),
                    ["variant0"] = VariantProp(x, y, width, height)
                },
                ["children"] = new JsonArray()
            };

            // Add type-specific defaults
            foreach (var kv in controlSpec.Defaults)
                newControl["properties"]![kv.Key] = kv.Value;

            // Add to parent's children
            var children = parentNode["children"] as JsonArray;
            if (children == null)
            {
                children = new JsonArray();
                parentNode["children"] = children;
            }
            children.Add(newControl);

            // Regenerate manifest
            json["manifest"] = CollectManifest(rootControl);

            // Backup and write
            string backupPath = layoutPath + ".bak";
            File.Copy(layoutPath, backupPath, overwrite: true);

            byte[] output = BalEncoder.Encode(json.ToJsonString());
            File.WriteAllBytes(layoutPath, output);

            return JsonSerializer.Serialize(new
            {
                success = true,
                path = layoutPath,
                backup = backupPath,
                controlAdded = new { name = finalName, type = controlType, position = $"{x},{y}", size = $"{width}x{height}" }
            }, JsonOptions.Default);
        }

        // ── Remove Control ───────────────────────────────────────────

        [McpServerTool, Description("Removes one or more controls from a layout by name. Names can be a single string or comma-separated list. Creates .bak backup first.")]
        public static string LayoutRemoveControl(
            [Description("Absolute path to the .bal or .bil layout file")] string layoutPath,
            [Description("Control name(s) to remove. Single name or comma-separated list.")] string controlNames)
        {
            PathSecurity.ValidateAbsolutePath(layoutPath, nameof(layoutPath));

            if (!File.Exists(layoutPath))
                throw new FileNotFoundException($"Layout file not found: {layoutPath}");

            var names = controlNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (names.Length == 0)
                throw new ArgumentException("No control names provided.");

            var data = File.ReadAllBytes(layoutPath);
            var decoded = BalDecoder.Decode(data);
            var json = JsonNode.Parse(decoded)?.AsObject();
            if (json == null)
                throw new FormatException("Decoded layout JSON root is not an object.");

            var rootControl = json["rootControl"] as JsonObject;
            if (rootControl == null)
                throw new InvalidOperationException("Layout has no rootControl");

            var removed = new List<string>();
            var notFound = new List<string>();
            var nameSet = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

            foreach (var name in names)
            {
                if (RemoveControlFromTree(rootControl, name))
                    removed.Add(name);
                else
                    notFound.Add(name);
            }

            // Regenerate manifest
            json["manifest"] = CollectManifest(rootControl);

            // Backup and write
            string backupPath = layoutPath + ".bak";
            File.Copy(layoutPath, backupPath, overwrite: true);

            byte[] output = BalEncoder.Encode(json.ToJsonString());
            File.WriteAllBytes(layoutPath, output);

            return JsonSerializer.Serialize(new
            {
                success = true,
                path = layoutPath,
                backup = backupPath,
                removed,
                notFound = notFound.Count > 0 ? notFound : null
            }, JsonOptions.Default);
        }

        // ── Move / Resize Control ────────────────────────────────────

        [McpServerTool, Description("Moves and/or resizes a control in a layout. Only specify the properties you want to change — omitted values keep their current setting. Creates .bak backup first.")]
        public static string LayoutMoveControl(
            [Description("Absolute path to the .bal or .bil layout file")] string layoutPath,
            [Description("Control name to modify")] string controlName,
            [Description("New X position (omit to keep current)")] int? left = null,
            [Description("New Y position (omit to keep current)")] int? top = null,
            [Description("New width (omit to keep current)")] int? width = null,
            [Description("New height (omit to keep current)")] int? height = null)
        {
            PathSecurity.ValidateAbsolutePath(layoutPath, nameof(layoutPath));

            if (!File.Exists(layoutPath))
                throw new FileNotFoundException($"Layout file not found: {layoutPath}");

            if (left == null && top == null && width == null && height == null)
                throw new ArgumentException("At least one of left, top, width, or height must be specified.");

            var data = File.ReadAllBytes(layoutPath);
            var decoded = BalDecoder.Decode(data);
            var json = JsonNode.Parse(decoded)?.AsObject();
            if (json == null)
                throw new FormatException("Decoded layout JSON root is not an object.");

            var rootControl = json["rootControl"] as JsonObject;
            if (rootControl == null)
                throw new InvalidOperationException("Layout has no rootControl");

            var node = FindControlByName(rootControl, controlName);
            if (node == null)
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Control '{controlName}' not found in layout."
                }, JsonOptions.Default);

            var props = node["properties"] as JsonObject;
            if (props == null)
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Control '{controlName}' has no properties."
                }, JsonOptions.Default);

            // Update variant0 (primary position). Hoist varObj out of the inner pattern-match
            // so the post-write response can read back the exact same axis values that just got
            // written — avoids a duplicate-variable compile error and keeps the fallback honest.
            var variant0 = props["variant0"] as JsonObject;
            JsonObject? varObj = variant0?["value"] as JsonObject;
            if (varObj != null)
            {
                if (left.HasValue) SetVariantInt(varObj, "left", left.Value);
                if (top.HasValue) SetVariantInt(varObj, "top", top.Value);
                if (width.HasValue) SetVariantInt(varObj, "width", width.Value);
                if (height.HasValue) SetVariantInt(varObj, "height", height.Value);
            }

            string backupPath = layoutPath + ".bak";
            File.Copy(layoutPath, backupPath, overwrite: true);

            byte[] output = BalEncoder.Encode(json.ToJsonString());
            File.WriteAllBytes(layoutPath, output);

            // Build the response from the parameter values the caller just asked for, falling
            // back to the PRE-MOVE variants from variant0.value.* for any unspecified axis.
            // Don't read props["left"] directly: SetVariantInt writes to variant0.value.left,
            // and props["left"] only exists when the file happened to also store the flat
            // Int32 alongside — most layouts don't, so props["left"] would yield the default 0.
            int prevLeft = ReadVariantInt(varObj, "left");
            int prevTop = ReadVariantInt(varObj, "top");
            int prevWidth = ReadVariantInt(varObj, "width");
            int prevHeight = ReadVariantInt(varObj, "height");

            int finalLeft = left ?? prevLeft;
            int finalTop = top ?? prevTop;
            int finalWidth = width ?? prevWidth;
            int finalHeight = height ?? prevHeight;

            return JsonSerializer.Serialize(new
            {
                success = true,
                path = layoutPath,
                backup = backupPath,
                control = controlName,
                newPosition = (left.HasValue || top.HasValue) ? $"{finalLeft},{finalTop}" : null,
                newSize = (width.HasValue || height.HasValue) ? $"{finalWidth}x{finalHeight}" : null
            }, JsonOptions.Default);
        }

        // ── Validation ───────────────────────────────────────────────

        private static List<string> ValidateLayoutJson(JsonObject json)
        {
            var errors = new List<string>();

            string[] required = { "version", "gridSize", "variants", "manifest", "fileReferences", "scriptData", "flags", "rootControl" };
            foreach (var field in required)
                if (json[field] == null)
                    errors.Add($"Missing required field: '{field}'");

            if (json["variants"] is JsonArray variants)
            {
                for (int i = 0; i < variants.Count; i++)
                {
                    var v = variants[i] as JsonObject;
                    if (v == null) { errors.Add($"variants[{i}]: not an object"); continue; }
                    if (v["scale"] == null) errors.Add($"variants[{i}]: missing 'scale'");
                    if (v["width"] == null) errors.Add($"variants[{i}]: missing 'width'");
                    if (v["height"] == null) errors.Add($"variants[{i}]: missing 'height'");
                }
            }

            if (json["manifest"] is JsonArray manifest)
            {
                for (int i = 0; i < manifest.Count; i++)
                {
                    var m = manifest[i] as JsonObject;
                    if (m == null) { errors.Add($"manifest[{i}]: not an object"); continue; }
                    if (m["name"] == null) errors.Add($"manifest[{i}]: missing 'name'");
                    if (m["javaType"] == null) errors.Add($"manifest[{i}]: missing 'javaType'");
                    if (m["csType"] == null) errors.Add($"manifest[{i}]: missing 'csType'");
                }
            }

            if (json["flags"] is JsonObject flags)
            {
                if (flags["c"] == null || !IsBoolean(flags["c"]))
                    errors.Add("flags: missing or invalid 'c' (must be boolean)");
                if (flags["d"] == null || !IsBoolean(flags["d"]))
                    errors.Add("flags: missing or invalid 'd' (must be boolean)");
            }

            if (json["rootControl"] != null && json["rootControl"]?.GetValueKind() != JsonValueKind.Object)
                errors.Add("rootControl: must be an object");

            if (json["rootControl"] is JsonObject rootControl)
            {
                ValidateControlNode(rootControl, "rootControl", errors);
                ValidatePropertiesFormat(rootControl, "rootControl", errors);
            }

            return errors;
        }

        private static bool IsBoolean(JsonNode? node)
        {
            var kind = node?.GetValueKind();
            return kind == JsonValueKind.True || kind == JsonValueKind.False;
        }

        private static void ValidateControlNode(JsonObject node, string path, List<string> errors)
        {
            if (node["properties"] == null)
                errors.Add($"{path}: missing 'properties'");
            else if (node["properties"]?.GetValueKind() != JsonValueKind.Object)
                errors.Add($"{path}.properties: must be an object");

            if (node["children"] == null)
                errors.Add($"{path}: missing 'children'");
            else if (node["children"] is JsonArray children)
            {
                for (int i = 0; i < children.Count; i++)
                {
                    if (children[i] is JsonObject child)
                        ValidateControlNode(child, $"{path}.children[{i}]", errors);
                    else
                        errors.Add($"{path}.children[{i}]: not an object");
                }
            }
            else
                errors.Add($"{path}.children: must be an array");
        }



        private static void ValidatePropertiesFormat(JsonObject node, string path, List<string> errors)
        {
            var props = node["properties"] as JsonObject;
            if (props == null) return;

            foreach (var prop in props)
            {
                if (prop.Value is JsonObject propObj)
                {
                    // Check if it's a tagged value or a nested object
                    if (propObj["tag"] == null && propObj["csType"] == null && propObj["type"] == null)
                    {
                        // It's neither a tagged value nor a drawable-like nested object
                        // Check if the value looks like a raw string/number (missing tag wrapper)
                        if (propObj["value"] == null && propObj.Any())
                        {
                            // Has children but no "tag" and no "value" — might be an Object without tag
                            // This is OK for variant0 and drawable
                            if (prop.Key != "variant0" && prop.Key != "drawable" && prop.Key != "padding")
                            {
                                errors.Add($"{path}.{prop.Key}: property value is missing 'tag' wrapper. Expected format: {{\"tag\": \"StringRef\", \"value\": \"...\"}} but got a raw object.");
                            }
                        }
                    }
                }
                else if (prop.Value is JsonValue)
                {
                    // Raw value without tag wrapper
                    errors.Add($"{path}.{prop.Key}: property is a raw value '{prop.Value}'. Must be wrapped as {{\"tag\": \"...\", \"value\": ...}}.");
                }
            }

            // Recurse into children
            if (node["children"] is JsonArray children)
            {
                for (int i = 0; i < children.Count; i++)
                {
                    if (children[i] is JsonObject child)
                        ValidatePropertiesFormat(child, $"{path}.children[{i}]", errors);
                }
            }
        }

        // ── Control Type Registry ────────────────────────────────────

        private class ControlSpec
        {
            public string CsType { get; set; } = "";
            public string Type { get; set; } = "";
            public string JavaType { get; set; } = "";
            public Dictionary<string, JsonObject> Defaults { get; set; } = new();
        }

        private static ControlSpec? GetControlDefaults(string type, bool isB4J)
        {
            type = type.ToLowerInvariant().Trim();

            return type switch
            {
                "button" => new ControlSpec
                {
                    CsType = "Dbasic.Designer.MetaButton",
                    Type = isB4J ? "javafx.scene.control.Button" : ".ButtonWrapper",
                    JavaType = isB4J ? "javafx.scene.control.Button" : "anywheresoftware.b4a.objects.ButtonWrapper",
                    Defaults = new Dictionary<string, JsonObject>
                    {
                        ["text"] = Prop("StringRef", ""),
                        ["textColor"] = Prop("Color", "#FFF0F8FF"),
                        ["enabled"] = Prop("Bool", true)
                    }
                },
                "label" => new ControlSpec
                {
                    CsType = "Dbasic.Designer.MetaLabel",
                    Type = isB4J ? "javafx.scene.control.Label" : ".LabelWrapper",
                    JavaType = isB4J ? "javafx.scene.control.Label" : "anywheresoftware.b4a.objects.LabelWrapper",
                    Defaults = new Dictionary<string, JsonObject>
                    {
                        ["text"] = Prop("StringRef", ""),
                        ["textColor"] = Prop("Color", "#FFF0F8FF"),
                        ["multiline"] = Prop("Bool", false),
                        ["adjustFontSizeToFit"] = Prop("Bool", false)
                    }
                },
                "edittext" or "textfield" => new ControlSpec
                {
                    CsType = isB4J ? "Dbasic.Designer.MetaTextField" : "Dbasic.Designer.MetaEditText",
                    Type = isB4J ? "javafx.scene.control.TextField" : ".EditTextWrapper",
                    JavaType = isB4J ? "javafx.scene.control.TextField" : "anywheresoftware.b4a.objects.EditTextWrapper",
                    Defaults = new Dictionary<string, JsonObject>
                    {
                        ["text"] = Prop("StringRef", ""),
                        ["hintText"] = Prop("StringRef", ""),
                        ["textColor"] = Prop("Color", "#FFF0F8FF"),
                        ["passwordMode"] = Prop("Bool", false),
                        ["enabled"] = Prop("Bool", true)
                    }
                },
                "panel" or "pane" => new ControlSpec
                {
                    CsType = isB4J ? "Dbasic.Designer.MetaPane" : "Dbasic.Designer.MetaPanel",
                    Type = isB4J ? "javafx.scene.layout.Pane" : ".PanelWrapper",
                    JavaType = isB4J ? "javafx.scene.layout.Pane" : "anywheresoftware.b4a.objects.PanelWrapper",
                    Defaults = new Dictionary<string, JsonObject>()
                },
                "checkbox" => new ControlSpec
                {
                    CsType = "Dbasic.Designer.MetaCheckBox",
                    Type = isB4J ? "javafx.scene.control.CheckBox" : ".CheckBoxWrapper",
                    JavaType = isB4J ? "javafx.scene.control.CheckBox" : "anywheresoftware.b4a.objects.CheckBoxWrapper",
                    Defaults = new Dictionary<string, JsonObject>
                    {
                        ["text"] = Prop("StringRef", ""),
                        ["textColor"] = Prop("Color", "#FFF0F8FF"),
                        ["checked"] = Prop("Bool", false),
                        ["enabled"] = Prop("Bool", true)
                    }
                },
                "imageview" or "image" => new ControlSpec
                {
                    CsType = "Dbasic.Designer.MetaImageView",
                    Type = isB4J ? "javafx.scene.image.ImageView" : ".ImageViewWrapper",
                    JavaType = isB4J ? "javafx.scene.image.ImageView" : "anywheresoftware.b4a.objects.ImageViewWrapper",
                    Defaults = new Dictionary<string, JsonObject>
                    {
                        ["imageFile"] = Prop("StringRef", ""),
                        ["contentMode"] = Prop("Int32", 0)
                    }
                },
                "scrollview" or "scroll" => new ControlSpec
                {
                    CsType = isB4J ? "Dbasic.Designer.MetaScrollPane" : "Dbasic.Designer.MetaScrollView",
                    Type = isB4J ? "javafx.scene.control.ScrollPane" : ".ScrollViewWrapper",
                    JavaType = isB4J ? "javafx.scene.control.ScrollPane" : "anywheresoftware.b4a.objects.ScrollViewWrapper",
                    Defaults = new Dictionary<string, JsonObject>
                    {
                        ["contentWidth"] = Prop("Int32", 100),
                        ["contentHeight"] = Prop("Int32", 500),
                        ["pagingEnabled"] = Prop("Bool", false),
                        ["bounces"] = Prop("Bool", true)
                    }
                },
                "webview" or "web" => new ControlSpec
                {
                    CsType = "Dbasic.Designer.MetaWebView",
                    Type = isB4J ? "javafx.scene.web.WebView" : ".WebViewWrapper",
                    JavaType = isB4J ? "javafx.scene.web.WebView" : "anywheresoftware.b4a.objects.WebViewWrapper",
                    Defaults = new Dictionary<string, JsonObject>
                    {
                        ["suppressRendering"] = Prop("Bool", false)
                    }
                },
                "switch" or "toggle" => new ControlSpec
                {
                    CsType = "Dbasic.Designer.MetaCheckBox",
                    Type = isB4J ? "javafx.scene.control.CheckBox" : ".CheckBoxWrapper",
                    JavaType = isB4J ? "javafx.scene.control.CheckBox" : "anywheresoftware.b4a.objects.CheckBoxWrapper",
                    Defaults = new Dictionary<string, JsonObject>
                    {
                        ["value"] = Prop("Bool", false),
                        ["onColor"] = Prop("Color", "#FFF0F8FF"),
                        ["enabled"] = Prop("Bool", true)
                    }
                },
                "slider" or "seekbar" => new ControlSpec
                {
                    CsType = isB4J ? "Dbasic.Designer.MetaSlider" : "Dbasic.Designer.MetaSeekBar",
                    Type = isB4J ? "javafx.scene.control.Slider" : ".SeekBarWrapper",
                    JavaType = isB4J ? "javafx.scene.control.Slider" : "anywheresoftware.b4a.objects.SeekBarWrapper",
                    Defaults = new Dictionary<string, JsonObject>
                    {
                        ["value"] = Prop("Float", 50),
                        ["minimumValue"] = Prop("Float", 0),
                        ["maximumValue"] = Prop("Float", 100),
                        ["continuous"] = Prop("Bool", true),
                        ["enabled"] = Prop("Bool", true)
                    }
                },
                "spinner" or "picker" or "combobox" => new ControlSpec
                {
                    CsType = isB4J ? "Dbasic.Designer.MetaComboBox" : "Dbasic.Designer.MetaSpinner",
                    Type = isB4J ? "javafx.scene.control.ComboBox" : ".SpinnerWrapper",
                    JavaType = isB4J ? "javafx.scene.control.ComboBox" : "anywheresoftware.b4a.objects.SpinnerWrapper",
                    Defaults = new Dictionary<string, JsonObject>
                    {
                        ["enabled"] = Prop("Bool", true)
                    }
                },
                "progressbar" or "progress" => new ControlSpec
                {
                    CsType = "Dbasic.Designer.MetaProgressBar",
                    Type = isB4J ? "javafx.scene.control.ProgressBar" : ".ProgressBarWrapper",
                    JavaType = isB4J ? "javafx.scene.control.ProgressBar" : "anywheresoftware.b4a.objects.ProgressDialogWrapper",
                    Defaults = new Dictionary<string, JsonObject>()
                },
                "radiobutton" => new ControlSpec
                {
                    CsType = "Dbasic.Designer.MetaRadioButton",
                    Type = ".RadioButtonWrapper",
                    JavaType = "anywheresoftware.b4a.objects.RadioButtonWrapper",
                    Defaults = new Dictionary<string, JsonObject>
                    {
                        ["text"] = Prop("StringRef", ""),
                        ["textColor"] = Prop("Color", "#FFF0F8FF"),
                        ["checked"] = Prop("Bool", false),
                        ["enabled"] = Prop("Bool", true)
                    }
                },
                "togglebutton" => new ControlSpec
                {
                    CsType = "Dbasic.Designer.MetaToggleButton",
                    Type = ".ToggleButtonWrapper",
                    JavaType = "anywheresoftware.b4a.objects.ToggleButtonWrapper",
                    Defaults = new Dictionary<string, JsonObject>
                    {
                        ["text"] = Prop("StringRef", ""),
                        ["textColor"] = Prop("Color", "#FFF0F8FF"),
                        ["checked"] = Prop("Bool", false),
                        ["enabled"] = Prop("Bool", true)
                    }
                },
                "datepicker" or "date" => new ControlSpec
                {
                    CsType = "Dbasic.Designer.MetaDatePicker",
                    Type = isB4J ? "javafx.scene.control.DatePicker" : ".DatePickerWrapper",
                    JavaType = isB4J ? "javafx.scene.control.DatePicker" : "anywheresoftware.b4a.objects.DatePickerWrapper",
                    Defaults = new Dictionary<string, JsonObject>()
                },
                "textview" or "textarea" => new ControlSpec
                {
                    CsType = "Dbasic.Designer.MetaTextArea",
                    Type = "javafx.scene.control.TextArea",
                    JavaType = "javafx.scene.control.TextArea",
                    Defaults = new Dictionary<string, JsonObject>
                    {
                        ["text"] = Prop("StringRef", ""),
                        ["textColor"] = Prop("Color", "#FFF0F8FF"),
                        ["editable"] = Prop("Bool", true)
                    }
                },
                "choicebox" => new ControlSpec
                {
                    CsType = "Dbasic.Designer.MetaChoiceBox",
                    Type = "javafx.scene.control.ChoiceBox",
                    JavaType = "javafx.scene.control.ChoiceBox",
                    Defaults = new Dictionary<string, JsonObject>
                    {
                        ["enabled"] = Prop("Bool", true)
                    }
                },
                "progressindicator" => new ControlSpec
                {
                    CsType = "Dbasic.Designer.MetaProgressIndicator",
                    Type = "javafx.scene.control.ProgressIndicator",
                    JavaType = "javafx.scene.control.ProgressIndicator",
                    Defaults = new Dictionary<string, JsonObject>()
                },
                "horizontalscrollview" => new ControlSpec
                {
                    CsType = "Dbasic.Designer.MetaHorizontalScrollView",
                    Type = ".HorizontalScrollViewWrapper",
                    JavaType = "anywheresoftware.b4a.objects.HorizontalScrollViewWrapper",
                    Defaults = new Dictionary<string, JsonObject>
                    {
                        ["contentWidth"] = Prop("Int32", 500),
                        ["contentHeight"] = Prop("Int32", 100)
                    }
                },
                _ => null
            };
        }

        // ── Tree Helpers ─────────────────────────────────────────────

        private static JsonObject? FindControlByName(JsonObject node, string name)
        {
            var props = node["properties"] as JsonObject;
            if (props != null)
            {
                var nodeName = GetPropString(props, "name") ?? GetPropString(props, "eventName");
                if (string.Equals(nodeName, name, StringComparison.OrdinalIgnoreCase))
                    return node;
            }

            if (node["children"] is JsonArray children)
            {
                foreach (var child in children)
                {
                    if (child is JsonObject childObj)
                    {
                        var found = FindControlByName(childObj, name);
                        if (found != null) return found;
                    }
                }
            }

            return null;
        }

        private static bool RemoveControlFromTree(JsonObject node, string name)
        {
            if (node["children"] is JsonArray children)
            {
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    if (children[i] is JsonObject child)
                    {
                        var props = child["properties"] as JsonObject;
                        var childName = props != null ? (GetPropString(props, "name") ?? GetPropString(props, "eventName")) : null;
                        if (string.Equals(childName, name, StringComparison.OrdinalIgnoreCase))
                        {
                            children.RemoveAt(i);
                            return true;
                        }
                        if (RemoveControlFromTree(child, name))
                            return true;
                    }
                }
            }
            return false;
        }

        private static void CollectControlNames(JsonObject node, HashSet<string> names)
        {
            var props = node["properties"] as JsonObject;
            if (props != null)
            {
                var name = GetPropString(props, "name") ?? GetPropString(props, "eventName");
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            }
            if (node["children"] is JsonArray children)
            {
                foreach (var child in children)
                    if (child is JsonObject childObj)
                        CollectControlNames(childObj, names);
            }
        }

        private static string GenerateControlName(string baseType, HashSet<string> existing)
        {
            for (int i = 1; i <= 99; i++)
            {
                var candidate = $"{baseType}{i}";
                if (!existing.Contains(candidate))
                    return candidate;
            }
            return $"{baseType}{Guid.NewGuid().ToString("N")[..4]}";
        }

        private static JsonArray CollectManifest(JsonObject rootControl)
        {
            var manifest = new JsonArray();
            CollectManifestRecursive(rootControl, manifest);
            return manifest;
        }

        private static void CollectManifestRecursive(JsonObject node, JsonArray manifest)
        {
            var props = node["properties"] as JsonObject;
            if (props != null)
            {
                var name = GetPropString(props, "name");
                var javaType = GetPropString(props, "javaType");
                var csType = GetPropString(props, "csType");
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(javaType) && !string.IsNullOrEmpty(csType))
                {
                    manifest.Add(new JsonObject
                    {
                        ["name"] = name,
                        ["javaType"] = javaType,
                        ["csType"] = csType
                    });
                }
            }
            if (node["children"] is JsonArray children)
            {
                foreach (var child in children)
                    if (child is JsonObject childObj)
                        CollectManifestRecursive(childObj, manifest);
            }
        }

        // ── Property Helpers ─────────────────────────────────────────

        private static JsonObject Prop(string tag, object value)
        {
            // Color comes as "#AARRGGBB" hex string — convert to {a, r, g, b} object
            if (tag == "Color")
            {
                string hex = value.ToString() ?? "FFFFFFFF";
                if (hex.StartsWith("#")) hex = hex.Substring(1);
                if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) hex = hex.Substring(2);
                if (hex.Length < 8) hex = hex.PadLeft(8, 'F');

                return new JsonObject
                {
                    ["tag"] = "Color",
                    ["a"] = Convert.ToInt32(hex.Substring(0, 2), 16),
                    ["r"] = Convert.ToInt32(hex.Substring(2, 2), 16),
                    ["g"] = Convert.ToInt32(hex.Substring(4, 2), 16),
                    ["b"] = Convert.ToInt32(hex.Substring(6, 2), 16)
                };
            }

            return new JsonObject { ["tag"] = tag, ["value"] = JsonValue.Create(value) };
        }

        private static JsonObject VariantProp(int left, int top, int width, int height)
        {
            return new JsonObject
            {
                ["tag"] = "Object",
                ["value"] = new JsonObject
                {
                    ["left"] = new JsonObject { ["tag"] = "Int32", ["value"] = left },
                    ["top"] = new JsonObject { ["tag"] = "Int32", ["value"] = top },
                    ["width"] = new JsonObject { ["tag"] = "Int32", ["value"] = width },
                    ["height"] = new JsonObject { ["tag"] = "Int32", ["value"] = height },
                    ["hanchor"] = new JsonObject { ["tag"] = "Int32", ["value"] = 0 },
                    ["vanchor"] = new JsonObject { ["tag"] = "Int32", ["value"] = 0 }
                }
            };
        }

        private static string? GetPropString(JsonObject? props, string key)
        {
            if (props == null || !props.TryGetPropertyValue(key, out var token)) return null;

            // Wrapped format: { "tag": "StringRef", "value": "..." }
            if (token is JsonObject obj && obj.TryGetPropertyValue("value", out var val) && val is JsonValue jv && jv.TryGetValue<string>(out var wrapped))
                return wrapped;

            // Flat format: "key": "value" (from decoder output)
            if (token is JsonValue jval && jval.TryGetValue<string>(out var flat))
                return flat;

            return null;
        }

        private static int GetVariantInt(JsonObject? props, string key, int def)
        {
            if (props == null || !props.TryGetPropertyValue(key, out var token)) return def;

            // Wrapped format: { "tag": "Int32", "value": 100 }
            if (token is JsonObject obj)
            {
                if (obj.TryGetPropertyValue("value", out var val) && val is JsonValue jv && jv.TryGetValue<int>(out int i))
                    return i;
            }

            // Flat format: "key": 100 (from decoder output)
            if (token is JsonValue jval && jval.TryGetValue<int>(out int j))
                return j;

            return def;
        }

        private static void SetVariantInt(JsonObject varObj, string key, int value)
        {
            varObj[key] = new JsonObject { ["tag"] = "Int32", ["value"] = value };
        }

        // Read an Int32 axis from variant0.value.<key>.value — the actual persisted location.
        // Mirrors SetVariantInt so response values track what was just written.
        private static int ReadVariantInt(JsonObject? varObj, string key)
        {
            if (varObj == null || !varObj.TryGetPropertyValue(key, out var prop)) return 0;
            if (prop is JsonObject obj && obj.TryGetPropertyValue("value", out var v) && v is JsonValue jv && jv.TryGetValue<int>(out int i))
                return i;
            return 0;
        }

        [McpServerTool, Description("Generates B4X code for a layout control: inserts a Dim declaration in the appropriate Globals section and/or appends an event Sub skeleton at the end of the file. Reads the control type from the layout to produce correct type annotations (e.g. Private btn As B4XView). Creates .bak backup before modifying.")]
        public static string GenerateCodeFromLayout(
    [Description("Absolute path to the .bal or .bjl layout file")] string layoutPath,
    [Description("Control name from the layout (e.g. 'Button1', 'lblCounter')")] string controlName,
    [Description("Absolute path to the target .bas or .b4a/.b4j file to insert code into")] string sourcePath,
    [Description("What to generate: 'dim' (declaration only), 'event' (Sub skeleton only), or 'both' (default)")] string generate = "both")
        {
            PathSecurity.ValidateAbsolutePath(layoutPath, nameof(layoutPath));
            PathSecurity.ValidateAbsolutePath(sourcePath, nameof(sourcePath));

            if (!File.Exists(layoutPath))
                throw new FileNotFoundException($"Layout file not found: {layoutPath}");
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException($"Source file not found: {sourcePath}");

            generate = generate.ToLowerInvariant();

            // ── Read and decode layout ────────────────────────────────────
            var data = File.ReadAllBytes(layoutPath);
            var decoded = BalDecoder.Decode(data);
            var json = JsonNode.Parse(decoded)?.AsObject();
            if (json == null)
                throw new FormatException("Decoded layout JSON root is not an object.");

            var rootControl = json["rootControl"] as JsonObject;
            if (rootControl == null)
                throw new InvalidOperationException("Layout has no rootControl");

            var node = FindControlByName(rootControl, controlName);
            if (node == null)
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Control '{controlName}' not found in layout.",
                    hint = "Use list_layout_controls to see all control names."
                }, JsonOptions.Default);

            var props = node["properties"] as JsonObject;
            if (props == null)
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    error = $"Control '{controlName}' has no properties."
                }, JsonOptions.Default);

            string controlType = GetPropString(props, "javaType") ?? GetPropString(props, "csType") ?? "B4XView";
            string eventName = GetPropString(props, "eventName") ?? controlName;
            controlType = SimplifyTypeName(controlType);

            // ── Read source file ──────────────────────────────────────────
            string raw = File.ReadAllText(sourcePath);

            const string marker = "@EndOfDesignText@";
            int markerIdx = raw.IndexOf(marker, StringComparison.Ordinal);
            string header = markerIdx >= 0 ? raw.Substring(0, markerIdx + marker.Length) : "";
            string source = markerIdx >= 0 ? raw.Substring(markerIdx + marker.Length).TrimStart('\r', '\n') : raw;
            var lines = source.Replace("\r\n", "\n").Split('\n').ToList();

            var results = new List<object>();
            int linesAdded = 0;

            // ── Generate Dim ──────────────────────────────────────────────
            if (generate == "dim" || generate == "both")
            {
                string dimCode = $"Private {controlName} As {controlType}";

                // Check if already declared
                var existingPattern = new Regex($@"^\s*(?:Private|Dim)\s+{Regex.Escape(controlName)}\s+As\s+\S+",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline, RegexTimeout);
                var existingMatch = existingPattern.Match(source);

                if (existingMatch.Success)
                {
                    results.Add(new { action = "dim", status = "already_exists", existing = existingMatch.Value.Trim() });
                }
                else
                {
                    // Find insertion point: Class_Globals > Process_Globals > Globals
                    int insertLine = FindGlobalsInsertionLine(lines);
                    if (insertLine >= 0)
                    {
                        lines.Insert(insertLine + 1, $"\t{dimCode}");
                        linesAdded++;
                        results.Add(new { action = "dim", status = "inserted", line = insertLine + 2, code = dimCode });
                    }
                    else
                    {
                        results.Add(new { action = "dim", status = "failed", error = "Could not find Sub Globals, Sub Process_Globals, or Sub Class_Globals" });
                    }
                }
            }

            // ── Generate Event Subs ───────────────────────────────────────
            if (generate == "event" || generate == "both")
            {
                var events = GetDefaultEvents(controlType);

                foreach (var ev in events)
                {
                    string subName = $"{eventName}_{ev.Name}";
                    string subCode = ev.Parameters != null
                        ? $"Sub {subName}({ev.Parameters})\n\t\nEnd Sub"
                        : $"Sub {subName}\n\t\nEnd Sub";

                    // Check if already exists
                    var existingSub = new Regex($@"^\s*(?:Private\s+)?Sub\s+{Regex.Escape(subName)}\b",
                        RegexOptions.IgnoreCase | RegexOptions.Multiline, RegexTimeout);

                    if (existingSub.IsMatch(string.Join("\n", lines)))
                    {
                        results.Add(new { action = "event", eventName = ev.Name, subName, status = "already_exists" });
                        continue;
                    }

                    // Append to end of file
                    lines.Add("");
                    lines.Add(subCode);
                    linesAdded++;
                    results.Add(new { action = "event", eventName = ev.Name, subName, status = "appended", code = subCode });
                }
            }

            // ── Write back ────────────────────────────────────────────────
            string backupPath = sourcePath + ".bak";
            File.Copy(sourcePath, backupPath, overwrite: true);

            var updatedSource = string.Join("\n", lines);
            var finalContent = markerIdx >= 0 ? header + "\n" + updatedSource : updatedSource;
            File.WriteAllText(sourcePath, finalContent);

            return JsonSerializer.Serialize(new
            {
                success = true,
                sourcePath,
                backup = backupPath,
                controlName,
                controlType,
                eventPrefix = eventName,
                changes = results,
                totalLinesAdded = linesAdded
            }, JsonOptions.Default);
        }

        // ── Helpers for GenerateCodeFromLayout ──────────────────────────

        private static int FindGlobalsInsertionLine(List<string> lines)
        {
            var globalsPattern = new Regex(@"^\s*Sub\s+(Class_Globals|Process_Globals|Globals)\b", RegexOptions.IgnoreCase, RegexTimeout);
            int bestLine = -1;
            int bestPriority = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                var match = globalsPattern.Match(lines[i]);
                if (!match.Success) continue;

                int priority = match.Groups[1].Value.ToLowerInvariant() switch
                {
                    "class_globals" => 3,
                    "process_globals" => 2,
                    "globals" => 1,
                    _ => 0
                };

                if (priority > bestPriority)
                {
                    bestPriority = priority;
                    bestLine = i;
                }
            }

            return bestLine;
        }

        private static string SimplifyTypeName(string fullType)
        {
            if (string.IsNullOrEmpty(fullType)) return "B4XView";

            fullType = fullType.TrimStart('.').ToLowerInvariant();

            if (fullType.Contains("buttonwrapper")) return "Button";
            if (fullType.Contains("labelwrapper")) return "Label";
            if (fullType.Contains("edittextwrapper") || fullType.Contains("textfieldwrapper")) return "EditText";
            if (fullType.Contains("panelwrapper") || fullType.Contains("panewrapper")) return "Panel";
            if (fullType.Contains("checkboxwrapper")) return "CheckBox";
            if (fullType.Contains("imageviewwrapper")) return "ImageView";
            if (fullType.Contains("scrollviewwrapper")) return "ScrollView";
            if (fullType.Contains("webviewwrapper")) return "WebView";
            if (fullType.Contains("seekbarwrapper") || fullType.Contains("sliderwrapper")) return "Slider";
            if (fullType.Contains("spinnerwrapper") || fullType.Contains("comboboxwrapper")) return "Spinner";
            if (fullType.Contains("progressbarwrapper") || fullType.Contains("progressviewwrapper")) return "ProgressBar";
            if (fullType.Contains("datepickerwrapper")) return "DatePicker";
            if (fullType.Contains("activitywrapper")) return "Activity";

            return "B4XView";
        }

        private class EventDef
        {
            public string Name { get; set; } = "";
            public string? Parameters { get; set; }
        }

        private static List<EventDef> GetDefaultEvents(string controlType)
        {
            return controlType.ToLowerInvariant() switch
            {
                "button" => new List<EventDef> { new() { Name = "Click" } },
                "label" => new List<EventDef> { new() { Name = "Click" } },
                "edittext" => new List<EventDef>
        {
            new() { Name = "TextChanged", Parameters = "Old As String, New As String" },
            new() { Name = "EnterPressed" }
        },
                "checkbox" => new List<EventDef> { new() { Name = "CheckedChange", Parameters = "Checked As Boolean" } },
                "panel" => new List<EventDef> { new() { Name = "Click" }, new() { Name = "Touch", Parameters = "Action As Int, X As Float, Y As Float" } },
                "imageview" => new List<EventDef> { new() { Name = "Click" } },
                "switch" => new List<EventDef> { new() { Name = "CheckedChange", Parameters = "Checked As Boolean" } },
                "slider" => new List<EventDef> { new() { Name = "ValueChanged", Parameters = "Value As Int" } },
                "spinner" => new List<EventDef> { new() { Name = "ItemClick", Parameters = "Position As Int, Value As Object" } },
                "scrollview" => new List<EventDef> { new() { Name = "ScrollChanged", Parameters = "Position As Int" } },
                _ => new List<EventDef>()
            };
        }
        // ── Register Layout in Project ──────────────────────────────────

        [McpServerTool, Description("Registers a layout file in the project metadata so the IDE and builder recognize it. Adds FileN= and FileGroupN= entries to the project header, updates NumberOfFiles, and creates .bak backup. If the layout is already registered, does nothing.")]
        public static string RegisterLayoutInProject(
            [Description("Absolute path to the .b4a or .b4j project file")] string projectPath,
            [Description("Layout file name (e.g. 'Main.bal', 'Settings.bjl'). Can be just the filename or a relative path like 'Files/Main.bal'.")] string layoutFileName)
        {
            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));

            if (!File.Exists(projectPath))
                throw new FileNotFoundException($"Project file not found: {projectPath}");

            var ext = Path.GetExtension(projectPath).ToLowerInvariant();
            if (ext != ".b4a" && ext != ".b4j" && ext != ".b4i")
                throw new ArgumentException("File must have .b4a, .b4j, or .b4i extension");

            string raw = File.ReadAllText(projectPath);
            const string marker = "@EndOfDesignText@";
            int markerIdx = raw.IndexOf(marker, StringComparison.Ordinal);

            if (markerIdx < 0)
                throw new InvalidOperationException("Project file is corrupted: missing internal section separator.");

            string headerSection = raw.Substring(0, markerIdx);
            string codeSection = raw.Substring(markerIdx);

            // Detect line endings
            bool usesCrLf = headerSection.Contains("\r\n");
            string eol = usesCrLf ? "\r\n" : "\n";
            var lines = headerSection.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();

            // Normalize the filename for comparison
            string normalizedName = layoutFileName.Replace('\\', '/').Trim();
            string baseName = Path.GetFileName(normalizedName); // Just the file name for comparison

            var fileRegex = new Regex(@"^File(\d+)=(.*)$", RegexOptions.IgnoreCase, RegexTimeout);
            var fileGroupRegex = new Regex(@"^FileGroup(\d+)=(.*)$", RegexOptions.IgnoreCase, RegexTimeout);
            var numberOfFilesRegex = new Regex(@"^NumberOfFiles=(\d+)$", RegexOptions.IgnoreCase, RegexTimeout);

            int maxFileIndex = 0;
            int firstFileGroupIndex = -1;
            bool alreadyRegistered = false;

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var fileMatch = fileRegex.Match(line);
                if (fileMatch.Success)
                {
                    int idx = int.Parse(fileMatch.Groups[1].Value);
                    if (idx > maxFileIndex) maxFileIndex = idx;

                    string existingFile = fileMatch.Groups[2].Value.Trim().Replace('\\', '/');
                    string existingBase = Path.GetFileName(existingFile);
                    if (string.Equals(existingBase, baseName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(existingFile, normalizedName, StringComparison.OrdinalIgnoreCase))
                    {
                        alreadyRegistered = true;
                    }
                    continue;
                }

                if (firstFileGroupIndex < 0 && fileGroupRegex.IsMatch(line))
                    firstFileGroupIndex = i;
            }

            if (alreadyRegistered)
            {
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    projectPath,
                    action = "already_registered",
                    layoutFile = normalizedName
                }, JsonOptions.Default);
            }

            // Add new FileN entry
            int nextIndex = maxFileIndex + 1;
            string fileLine = $"File{nextIndex}={normalizedName}";

            int insertIndex = firstFileGroupIndex >= 0 ? firstFileGroupIndex : lines.Count;
            lines.Insert(insertIndex, fileLine);

            // Add FileGroupN if groups exist
            bool hasGroups = lines.Any(l => fileGroupRegex.IsMatch(l));
            if (hasGroups)
            {
                // Find the end of the file group block
                int groupBlockEnd = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (fileGroupRegex.IsMatch(lines[i]))
                        groupBlockEnd = i + 1;
                }

                if (groupBlockEnd >= 0)
                    lines.Insert(groupBlockEnd, $"FileGroup{nextIndex}=Default Group");
            }

            // Update NumberOfFiles
            bool updatedNumberOfFiles = false;
            for (int i = 0; i < lines.Count; i++)
            {
                var nfMatch = numberOfFilesRegex.Match(lines[i]);
                if (nfMatch.Success)
                {
                    lines[i] = $"NumberOfFiles={nextIndex}";
                    updatedNumberOfFiles = true;
                    break;
                }
            }
            if (!updatedNumberOfFiles)
                lines.Insert(0, $"NumberOfFiles={nextIndex}");

            // Reconstruct and write
            string newHeader = string.Join(eol, lines);
            string newContent = newHeader + codeSection;

            string backupPath = projectPath + ".bak";
            File.Copy(projectPath, backupPath, overwrite: true);
            File.WriteAllText(projectPath, newContent);

            return JsonSerializer.Serialize(new
            {
                success = true,
                projectPath,
                backup = backupPath,
                action = "registered",
                layoutFile = normalizedName,
                entry = $"File{nextIndex}",
                numberOfFiles = nextIndex
            }, JsonOptions.Default);
        }
        [McpServerTool, Description("Registers a .bas module in the project metadata so the IDE and builder recognize it. Adds ModuleN= entry, updates NumberOfModules, and creates .bak backup. If the module is already registered, does nothing.")]
        public static string RegisterModuleInProject(
    [Description("Absolute path to the .b4a or .b4j project file")] string projectPath,
    [Description("Module file name (e.g. 'Settings.bas', 'Main'). Can include or omit .bas extension.")] string moduleName)
        {
            PathSecurity.ValidateAbsolutePath(projectPath, nameof(projectPath));

            if (!File.Exists(projectPath))
                throw new FileNotFoundException($"Project file not found: {projectPath}");

            var ext = Path.GetExtension(projectPath).ToLowerInvariant();
            if (ext != ".b4a" && ext != ".b4j" && ext != ".b4i")
                throw new ArgumentException("File must have .b4a, .b4j, or .b4i extension");

            string raw = File.ReadAllText(projectPath);
            const string marker = "@EndOfDesignText@";
            int markerIdx = raw.IndexOf(marker, StringComparison.Ordinal);

            if (markerIdx < 0)
                throw new InvalidOperationException("Project file is corrupted.");

            string headerSection = raw.Substring(0, markerIdx);
            string codeSection = raw.Substring(markerIdx);

            bool usesCrLf = headerSection.Contains("\r\n");
            string eol = usesCrLf ? "\r\n" : "\n";
            var lines = headerSection.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();

            // Normalize: strip .bas extension for comparison
            string normalizedName = moduleName.Replace('\\', '/').Trim();
            if (normalizedName.EndsWith(".bas", StringComparison.OrdinalIgnoreCase))
                normalizedName = normalizedName.Substring(0, normalizedName.Length - 4);

            var moduleRegex = new Regex(@"^Module(\d+)=(.*)$", RegexOptions.IgnoreCase, RegexTimeout);
            var numberOfModulesRegex = new Regex(@"^NumberOfModules=(\d+)$", RegexOptions.IgnoreCase, RegexTimeout);

            int maxModuleIndex = 0;
            bool alreadyRegistered = false;

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var moduleMatch = moduleRegex.Match(line);
                if (moduleMatch.Success)
                {
                    int idx = int.Parse(moduleMatch.Groups[1].Value);
                    if (idx > maxModuleIndex) maxModuleIndex = idx;

                    string existingName = moduleMatch.Groups[2].Value.Trim();
                    string existingNormalized = existingName.EndsWith(".bas", StringComparison.OrdinalIgnoreCase)
                        ? existingName.Substring(0, existingName.Length - 4)
                        : existingName;

                    if (string.Equals(existingNormalized, normalizedName, StringComparison.OrdinalIgnoreCase))
                        alreadyRegistered = true;
                }
            }

            if (alreadyRegistered)
            {
                return JsonSerializer.Serialize(new
                {
                    success = true,
                    projectPath,
                    action = "already_registered",
                    module = normalizedName
                }, JsonOptions.Default);
            }

            // Add new ModuleN entry
            int nextIndex = maxModuleIndex + 1;
            lines.Add($"Module{nextIndex}={normalizedName}");

            // Update NumberOfModules
            bool updatedNumberOfModules = false;
            for (int i = 0; i < lines.Count; i++)
            {
                var nmMatch = numberOfModulesRegex.Match(lines[i]);
                if (nmMatch.Success)
                {
                    lines[i] = $"NumberOfModules={nextIndex}";
                    updatedNumberOfModules = true;
                    break;
                }
            }
            if (!updatedNumberOfModules)
                lines.Insert(0, $"NumberOfModules={nextIndex}");

            string newHeader = string.Join(eol, lines);
            string newContent = newHeader + codeSection;

            string backupPath = projectPath + ".bak";
            File.Copy(projectPath, backupPath, overwrite: true);
            File.WriteAllText(projectPath, newContent);

            return JsonSerializer.Serialize(new
            {
                success = true,
                projectPath,
                backup = backupPath,
                action = "registered",
                module = normalizedName,
                entry = $"Module{nextIndex}",
                numberOfModules = nextIndex
            }, JsonOptions.Default);
        }
    }
}