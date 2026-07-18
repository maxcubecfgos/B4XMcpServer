using System;
using System.Collections.Generic;
using System.Linq;

namespace B4XEngineCore
{
    public class ControlTypeDef
    {
        public string DisplayName { get; set; } = "";
        public string MetaType { get; set; } = "";
        public Dictionary<Platform, string> JavaType { get; set; } = new();
        public Dictionary<Platform, string> CsType { get; set; } = new();
        public Dictionary<Platform, string> ShortTypeName { get; set; } = new();
        public (int Width, int Height) DefaultSize { get; set; } = (100, 50);
        public bool IsContainer { get; set; }
        public List<Platform> Platforms { get; set; } = new();
        public Dictionary<string, PropertyValue> Defaults { get; set; } = new();
        public List<string> NullableColorKeys { get; set; } = new();
        public Dictionary<Platform, List<string>> Events { get; set; } = new();
    }

    public static class ControlRegistry
    {
        private static readonly ColorValue AliceBlue = new(255, 240, 248, 255);
        private static readonly List<Platform> AllPlatforms = new() { Platform.B4A, Platform.B4J };

        private static readonly List<string> B4JNodeEvents = new()
        {
            "MouseClicked(EventData As MouseEvent)",
            "MouseMoved(EventData As MouseEvent)",
            "MouseDragged(EventData As MouseEvent)",
            "MousePressed(EventData As MouseEvent)",
            "MouseReleased(EventData As MouseEvent)",
            "MouseEntered(EventData As MouseEvent)",
            "MouseExited(EventData As MouseEvent)",
            "FocusChanged(HasFocus As Boolean)",
            "AnimationCompleted",
        };

        private static readonly List<string> B4JControlEvents = new()
        {
            "Resize(Width As Double, Height As Double)",
        };
        static ControlRegistry() { B4JControlEvents.AddRange(B4JNodeEvents); }

        private static readonly List<ControlTypeDef> Registry = new()
        {
            new ControlTypeDef
            {
                DisplayName = "Panel", MetaType = "MetaPanel",
                JavaType = new() { [Platform.B4A] = "anywheresoftware.b4a.objects.PanelWrapper", [Platform.B4J] = "javafx.scene.layout.Pane" },
                CsType = new() { [Platform.B4A] = "Dbasic.Designer.MetaPanel", [Platform.B4J] = "Dbasic.Designer.MetaPane" },
                ShortTypeName = new() { [Platform.B4A] = "Panel", [Platform.B4J] = "Pane" },
                DefaultSize = (200, 200), IsContainer = true, Platforms = AllPlatforms,
                Defaults = new() { ["backgroundColor"] = new ColorValue(255, 245, 245, 245), ["borderWidth"] = new FloatValue(1), ["cornerRadius"] = new FloatValue(3) },
                Events = new() { [Platform.B4A] = new() { "Touch(Action As Int, X As Float, Y As Float)", "Click", "LongClick" }, [Platform.B4J] = new() { "Resize(Width As Double, Height As Double)", "Touch(Action As Int, X As Float, Y As Float)" } },
                NullableColorKeys = new(),
            },
            new ControlTypeDef
            {
                DisplayName = "Label", MetaType = "MetaLabel",
                JavaType = new() { [Platform.B4A] = "anywheresoftware.b4a.objects.LabelWrapper", [Platform.B4J] = "javafx.scene.control.Label" },
                CsType = new() { [Platform.B4A] = "Dbasic.Designer.MetaLabel", [Platform.B4J] = "Dbasic.Designer.MetaLabel" },
                ShortTypeName = new() { [Platform.B4A] = "Label", [Platform.B4J] = "Label" },
                DefaultSize = (100, 40), IsContainer = false, Platforms = AllPlatforms,
                Defaults = new() { ["text"] = new StringRefValue(""), ["fontAwesome"] = new StringRefValue(""), ["materialIcons"] = new StringRefValue(""), ["textColor"] = AliceBlue, ["multiline"] = new BoolValue(false), ["adjustFontSizeToFit"] = new BoolValue(false), ["textAlignment"] = new IntValue(0) },
                NullableColorKeys = new() { "textColor" },
                Events = new() { [Platform.B4A] = new() { "Click", "LongClick" } },
            },
            new ControlTypeDef
            {
                DisplayName = "Button", MetaType = "MetaButton",
                JavaType = new() { [Platform.B4A] = "anywheresoftware.b4a.objects.ButtonWrapper", [Platform.B4J] = "javafx.scene.control.Button" },
                CsType = new() { [Platform.B4A] = "Dbasic.Designer.MetaButton", [Platform.B4J] = "Dbasic.Designer.MetaButton" },
                ShortTypeName = new() { [Platform.B4A] = "Button", [Platform.B4J] = "Button" },
                DefaultSize = (100, 40), IsContainer = false, Platforms = AllPlatforms,
                Defaults = new() { ["text"] = new StringRefValue(""), ["fontAwesome"] = new StringRefValue(""), ["materialIcons"] = new StringRefValue(""), ["style"] = new IntValue(0), ["textColor"] = AliceBlue, ["pressedTextColor"] = new ColorValue(255, 255, 255, 255), ["tintColor"] = AliceBlue, ["enabled"] = new BoolValue(true) },
                NullableColorKeys = new() { "textColor", "tintColor" },
                Events = new() { [Platform.B4A] = new() { "Click", "LongClick" }, [Platform.B4J] = new() { "Click" } },
            },
            new ControlTypeDef
            {
                DisplayName = "TextField", MetaType = "MetaTextField",
                JavaType = new() { [Platform.B4A] = "anywheresoftware.b4a.objects.EditTextWrapper", [Platform.B4J] = "javafx.scene.control.TextField" },
                CsType = new() { [Platform.B4A] = "Dbasic.Designer.MetaTextField", [Platform.B4J] = "Dbasic.Designer.MetaTextField" },
                ShortTypeName = new() { [Platform.B4A] = "EditText", [Platform.B4J] = "TextField" },
                DefaultSize = (150, 40), IsContainer = false, Platforms = AllPlatforms,
                Defaults = new() { ["text"] = new StringRefValue(""), ["textColor"] = AliceBlue, ["textAlignment"] = new IntValue(0), ["hintText"] = new StringRefValue(""), ["borderStyle"] = new IntValue(3), ["adjustFontSizeToFit"] = new BoolValue(false), ["showClearButton"] = new BoolValue(true), ["enabled"] = new BoolValue(true), ["passwordMode"] = new BoolValue(false) },
                NullableColorKeys = new() { "textColor" },
            },
            new ControlTypeDef
            {
                DisplayName = "ImageView", MetaType = "MetaImageView",
                JavaType = new() { [Platform.B4A] = "anywheresoftware.b4a.objects.ImageViewWrapper", [Platform.B4J] = "javafx.scene.image.ImageView" },
                CsType = new() { [Platform.B4A] = "Dbasic.Designer.MetaImageView", [Platform.B4J] = "Dbasic.Designer.MetaImageView" },
                ShortTypeName = new() { [Platform.B4A] = "ImageView", [Platform.B4J] = "ImageView" },
                DefaultSize = (100, 100), IsContainer = false, Platforms = AllPlatforms,
                Defaults = new() { ["imageFile"] = new StringRefValue(""), ["contentMode"] = new IntValue(0) },
                NullableColorKeys = new(),
            },
            new ControlTypeDef
            {
                DisplayName = "ScrollView", MetaType = "MetaScrollView",
                JavaType = new() { [Platform.B4A] = "anywheresoftware.b4a.objects.ScrollViewWrapper", [Platform.B4J] = "javafx.scene.control.ScrollPane" },
                CsType = new() { [Platform.B4A] = "Dbasic.Designer.MetaScrollView", [Platform.B4J] = "Dbasic.Designer.MetaScrollPane" },
                ShortTypeName = new() { [Platform.B4A] = "ScrollView", [Platform.B4J] = "ScrollPane" },
                DefaultSize = (100, 100), IsContainer = false, Platforms = AllPlatforms,
                Defaults = new() { ["contentWidth"] = new IntValue(100), ["contentHeight"] = new IntValue(500), ["pagingEnabled"] = new BoolValue(false), ["bounces"] = new BoolValue(true), ["showsVerticalIndicator"] = new BoolValue(true), ["showsHorizontalIndicator"] = new BoolValue(true) },
                NullableColorKeys = new(),
            },
            new ControlTypeDef
            {
                DisplayName = "WebView", MetaType = "MetaWebView",
                JavaType = new() { [Platform.B4A] = "anywheresoftware.b4a.objects.WebViewWrapper", [Platform.B4J] = "javafx.scene.web.WebView" },
                CsType = new() { [Platform.B4A] = "Dbasic.Designer.MetaWebView", [Platform.B4J] = "Dbasic.Designer.MetaWebView" },
                ShortTypeName = new() { [Platform.B4A] = "WebView", [Platform.B4J] = "WebView" },
                DefaultSize = (200, 200), IsContainer = false, Platforms = AllPlatforms,
                Defaults = new() { ["suppressRendering"] = new BoolValue(false) },
                NullableColorKeys = new(),
            },
            new ControlTypeDef
            {
                DisplayName = "Switch", MetaType = "MetaSwitch",
                JavaType = new() { [Platform.B4A] = "anywheresoftware.b4a.objects.CheckBoxWrapper", [Platform.B4J] = "javafx.scene.control.CheckBox" },
                CsType = new() { [Platform.B4A] = "Dbasic.Designer.MetaCheckBox", [Platform.B4J] = "Dbasic.Designer.MetaCheckBox" },
                ShortTypeName = new() { [Platform.B4A] = "CheckBox", [Platform.B4J] = "CheckBox" },
                DefaultSize = (100, 40), IsContainer = false, Platforms = AllPlatforms,
                Defaults = new() { ["value"] = new BoolValue(false), ["onColor"] = AliceBlue, ["offColor"] = AliceBlue, ["thumbColor"] = AliceBlue, ["enabled"] = new BoolValue(true) },
                NullableColorKeys = new() { "onColor", "offColor", "thumbColor" },
            },
            new ControlTypeDef
            {
                DisplayName = "Slider", MetaType = "MetaSlider",
                JavaType = new() { [Platform.B4A] = "anywheresoftware.b4a.objects.SeekBarWrapper", [Platform.B4J] = "javafx.scene.control.Slider" },
                CsType = new() { [Platform.B4A] = "Dbasic.Designer.MetaSeekBar", [Platform.B4J] = "Dbasic.Designer.MetaSlider" },
                ShortTypeName = new() { [Platform.B4A] = "SeekBar", [Platform.B4J] = "Slider" },
                DefaultSize = (150, 40), IsContainer = false, Platforms = AllPlatforms,
                Defaults = new() { ["value"] = new FloatValue(50), ["minimumValue"] = new FloatValue(0), ["maximumValue"] = new FloatValue(100), ["minimumTrackTintColor"] = AliceBlue, ["continuous"] = new BoolValue(true), ["enabled"] = new BoolValue(true) },
                NullableColorKeys = new() { "minimumTrackTintColor" },
            },
            new ControlTypeDef
            {
                DisplayName = "ProgressView", MetaType = "MetaProgressView",
                JavaType = new() { [Platform.B4A] = "anywheresoftware.b4a.objects.ProgressDialogWrapper", [Platform.B4J] = "javafx.scene.control.ProgressBar" },
                CsType = new() { [Platform.B4A] = "Dbasic.Designer.MetaProgressBar", [Platform.B4J] = "Dbasic.Designer.MetaProgressBar" },
                ShortTypeName = new() { [Platform.B4A] = "ProgressBar", [Platform.B4J] = "ProgressBar" },
                DefaultSize = (100, 30), IsContainer = false, Platforms = AllPlatforms,
                Defaults = new() { ["progressColor"] = AliceBlue },
                NullableColorKeys = new() { "progressColor" },
            },
            new ControlTypeDef
            {
                DisplayName = "CustomView", MetaType = "MetaCustomView",
                JavaType = new() { [Platform.B4A] = "anywheresoftware.b4a.objects.CustomViewWrapper", [Platform.B4J] = "javafx.scene.layout.Pane" },
                CsType = new() { [Platform.B4A] = "Dbasic.Designer.MetaCustomView", [Platform.B4J] = "Dbasic.Designer.MetaCustomView" },
                ShortTypeName = new() { [Platform.B4A] = "CustomView", [Platform.B4J] = "CustomView" },
                DefaultSize = (100, 40), IsContainer = false, Platforms = AllPlatforms,
                Defaults = new() { ["text"] = new StringRefValue(""), ["fontAwesome"] = new StringRefValue(""), ["materialIcons"] = new StringRefValue(""), ["textColor"] = AliceBlue, ["multiline"] = new BoolValue(false), ["adjustFontSizeToFit"] = new BoolValue(false), ["textAlignment"] = new IntValue(0) },
                NullableColorKeys = new() { "textColor" },
            },
            new ControlTypeDef
            {
                DisplayName = "TextView", MetaType = "MetaTextView",
                JavaType = new() { [Platform.B4J] = "javafx.scene.control.TextArea" },
                CsType = new() { [Platform.B4J] = "Dbasic.Designer.MetaTextArea" },
                ShortTypeName = new() { [Platform.B4J] = "TextArea" },
                DefaultSize = (150, 150), IsContainer = false, Platforms = new() { Platform.B4J },
                Defaults = new() { ["text"] = new StringRefValue(""), ["textColor"] = AliceBlue, ["textAlignment"] = new IntValue(0), ["editable"] = new BoolValue(true), ["borderWidth"] = new FloatValue(1), ["cornerRadius"] = new FloatValue(3), ["borderColor"] = new ColorValue(255, 128, 128, 128) },
                NullableColorKeys = new() { "textColor" },
            },
            new ControlTypeDef
            {
                DisplayName = "EditText", MetaType = "MetaEditText",
                JavaType = new() { [Platform.B4A] = "anywheresoftware.b4a.objects.EditTextWrapper" },
                CsType = new() { [Platform.B4A] = "Dbasic.Designer.MetaEditText" },
                ShortTypeName = new() { [Platform.B4A] = "EditText" },
                DefaultSize = (150, 40), IsContainer = false, Platforms = new() { Platform.B4A },
                Defaults = new() { ["text"] = new StringRefValue(""), ["textColor"] = AliceBlue, ["textAlignment"] = new IntValue(0), ["hintText"] = new StringRefValue(""), ["passwordMode"] = new BoolValue(false) },
                NullableColorKeys = new() { "textColor" },
            },
            new ControlTypeDef
            {
                DisplayName = "CheckBox", MetaType = "MetaCheckBox",
                JavaType = new() { [Platform.B4A] = "anywheresoftware.b4a.objects.CheckBoxWrapper" },
                CsType = new() { [Platform.B4A] = "Dbasic.Designer.MetaCheckBox" },
                ShortTypeName = new() { [Platform.B4A] = "CheckBox" },
                DefaultSize = (100, 40), IsContainer = false, Platforms = new() { Platform.B4A },
                Defaults = new() { ["text"] = new StringRefValue(""), ["textColor"] = AliceBlue, ["isChecked"] = new BoolValue(false), ["enabled"] = new BoolValue(true) },
                NullableColorKeys = new() { "textColor" },
            },
            new ControlTypeDef
            {
                DisplayName = "RadioButton", MetaType = "MetaRadioButton",
                JavaType = new() { [Platform.B4A] = "anywheresoftware.b4a.objects.RadioButtonWrapper" },
                CsType = new() { [Platform.B4A] = "Dbasic.Designer.MetaRadioButton" },
                ShortTypeName = new() { [Platform.B4A] = "RadioButton" },
                DefaultSize = (100, 40), IsContainer = false, Platforms = new() { Platform.B4A },
                Defaults = new() { ["text"] = new StringRefValue(""), ["textColor"] = AliceBlue, ["isChecked"] = new BoolValue(false), ["enabled"] = new BoolValue(true) },
                NullableColorKeys = new() { "textColor" },
            },
            new ControlTypeDef
            {
                DisplayName = "Spinner", MetaType = "MetaSpinner",
                JavaType = new() { [Platform.B4A] = "anywheresoftware.b4a.objects.SpinnerWrapper" },
                CsType = new() { [Platform.B4A] = "Dbasic.Designer.MetaSpinner" },
                ShortTypeName = new() { [Platform.B4A] = "Spinner" },
                DefaultSize = (150, 40), IsContainer = false, Platforms = new() { Platform.B4A },
                Defaults = new() { ["enabled"] = new BoolValue(true) },
                NullableColorKeys = new(),
            },
            new ControlTypeDef
            {
                DisplayName = "ComboBox", MetaType = "MetaComboBox",
                JavaType = new() { [Platform.B4J] = "javafx.scene.control.ComboBox" },
                CsType = new() { [Platform.B4J] = "Dbasic.Designer.MetaComboBox" },
                ShortTypeName = new() { [Platform.B4J] = "ComboBox" },
                DefaultSize = (150, 40), IsContainer = false, Platforms = new() { Platform.B4J },
                Defaults = new() { ["enabled"] = new BoolValue(true) },
                NullableColorKeys = new(),
            },
        };

        private static readonly Dictionary<string, ControlTypeDef> ByDisplayName = Registry.ToDictionary(d => d.DisplayName);
        private static readonly Dictionary<string, ControlTypeDef> ByMetaType = Registry.ToDictionary(d => d.MetaType);

        public static List<string> GetControlTypesForPlatform(Platform platform)
            => Registry.Where(d => d.Platforms.Contains(platform)).Select(d => d.DisplayName).OrderBy(n => n).ToList();

        public static ControlTypeDef? GetControlTypeByName(string displayName)
            => ByDisplayName.GetValueOrDefault(displayName);

        public static ControlTypeDef? GetControlTypeByMeta(string metaType)
            => ByMetaType.GetValueOrDefault(metaType);

        public static string GenerateControlName(string displayName, HashSet<string> existingNames)
        {
            for (int i = 1; ; i++)
            {
                string candidate = $"{displayName}{i}";
                if (!existingNames.Contains(candidate)) return candidate;
            }
        }

        public static ControlNode? CreateControl(string displayName, Platform platform, HashSet<string> existingNames, int x, int y, int variantCount, int sourceVariantIndex, int gridSize)
        {
            var def = ByDisplayName.GetValueOrDefault(displayName);
            if (def == null || !def.Platforms.Contains(platform)) return null;

            string name = GenerateControlName(displayName, existingNames);
            string javaType = def.JavaType.GetValueOrDefault(platform) ?? "";
            string csType = def.CsType.GetValueOrDefault(platform) ?? "";
            int snappedX = (int)(Math.Round((double)x / gridSize) * gridSize);
            int snappedY = (int)(Math.Round((double)y / gridSize) * gridSize);

            var props = new Dictionary<string, PropertyValue>
            {
                ["name"] = new StringRefValue(name),
                ["eventName"] = new StringRefValue(name),
                ["javaType"] = new StringRefValue(javaType),
                ["csType"] = new StringRefValue(csType),
                ["parent"] = new StringRefValue(""),
                ["visible"] = new BoolValue(true),
                ["tag"] = new StringRefValue(""),
            };

            foreach (var kvp in def.Defaults)
                props[kvp.Key] = ClonePropertyValue(kvp.Value);

            for (int vi = 0; vi < variantCount; vi++)
            {
                var vd = new Dictionary<string, PropertyValue>
                {
                    ["left"] = new IntValue(snappedX),
                    ["top"] = new IntValue(snappedY),
                    ["width"] = new IntValue(def.DefaultSize.Width),
                    ["height"] = new IntValue(def.DefaultSize.Height),
                    ["hanchor"] = new IntValue(0),
                    ["vanchor"] = new IntValue(0),
                };
                props[$"variant{vi}"] = new ObjectValue(vd);
            }

            return new ControlNode { Properties = props, Children = new() };
        }

        public static PropertyValue ClonePropertyValue(PropertyValue pv)
        {
            return pv switch
            {
                ObjectValue ov => new ObjectValue(ov.Value.ToDictionary(k => k.Key, k => ClonePropertyValue(k.Value))),
                ColorValue cv => new ColorValue(cv.A, cv.R, cv.G, cv.B),
                RectValue rv => new RectValue(rv.X, rv.Y, rv.Width, rv.Height),
                _ => pv,
            };
        }

        public static ControlNode DeepCloneControlNode(ControlNode node)
        {
            var newProps = new Dictionary<string, PropertyValue>();
            foreach (var kvp in node.Properties)
                newProps[kvp.Key] = ClonePropertyValue(kvp.Value);
            return new ControlNode { Properties = newProps, Children = node.Children.Select(DeepCloneControlNode).ToList() };
        }

        public static List<(string Name, string JavaType, string CsType)> CollectManifestEntries(ControlNode root)
        {
            var entries = new List<(string, string, string)>();
            CollectManifestRecursive(root, entries, true);
            return entries;
        }

        private static void CollectManifestRecursive(ControlNode node, List<(string, string, string)> entries, bool isRoot)
        {
            if (!isRoot)
            {
                string name = PropertyModel.GetStr(node, "name", "");
                string javaType = PropertyModel.GetStr(node, "javaType", "");
                string csType = PropertyModel.GetStr(node, "csType", "");
                if (!string.IsNullOrEmpty(name))
                    entries.Add((name, javaType, csType));
            }
            foreach (var child in node.Children)
                CollectManifestRecursive(child, entries, false);
        }
    }
}
