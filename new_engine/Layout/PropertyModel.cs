using System;
using System.Collections.Generic;
using System.Linq;

namespace B4XEngineCore
{
    public enum EditorType
    {
        String, Int, Double, Bool, Color, NullableColor, Dropdown, Rect, Font
    }

    public class PropertyDescriptor
    {
        public string Key { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public EditorType Editor { get; set; }
        public bool IsMergeable { get; set; }
        public bool IsReadOnly { get; set; }
        public List<(string Label, object Value)>? Options { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }
        public double? Step { get; set; }
        public bool AlphaEnabled { get; set; }
        public object? DefaultValue { get; set; }
    }

    public class PropertyData
    {
        public string Key { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public string Editor { get; set; } = "";
        public bool IsMergeable { get; set; }
        public bool IsReadOnly { get; set; }
        public object? Value { get; set; }
        public List<(string Label, object Value)>? Options { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }
        public double? Step { get; set; }
        public bool AlphaEnabled { get; set; }
    }

    public static class PropertyModel
    {
        public const int AnchorLeft = 0;
        public const int AnchorRight = 1;
        public const int AnchorBoth = 2;

        public static string GetStr(ControlNode node, string key, string def)
        {
            if (node.Properties.TryGetValue(key, out var v))
            {
                if (v is StringValue sv) return sv.Value;
                if (v is StringRefValue srv) return srv.Value;
            }
            return def;
        }

        public static int GetInt(ControlNode node, string key, int def)
        {
            if (node.Properties.TryGetValue(key, out var v))
            {
                if (v is IntValue iv) return iv.Value;
                if (v is FloatValue fv) return (int)Math.Round(fv.Value);
                if (v is DoubleValue dv) return (int)Math.Round(dv.Value);
            }
            return def;
        }

        public static double GetFloat(ControlNode node, string key, double def)
        {
            if (node.Properties.TryGetValue(key, out var v))
            {
                if (v is FloatValue fv) return fv.Value;
                if (v is DoubleValue dv) return dv.Value;
                if (v is IntValue iv) return iv.Value;
            }
            return def;
        }

        public static bool GetBool(ControlNode node, string key, bool def)
        {
            if (node.Properties.TryGetValue(key, out var v))
            {
                if (v is BoolValue bv) return bv.Value;
            }
            return def;
        }

        public static (byte A, byte R, byte G, byte B)? GetColor(ControlNode node, string key)
        {
            if (node.Properties.TryGetValue(key, out var v) && v is ColorValue cv)
                return (cv.A, cv.R, cv.G, cv.B);
            return null;
        }

        public static RectValue? GetRect(ControlNode node, string key)
        {
            if (node.Properties.TryGetValue(key, out var v) && v is RectValue rv)
                return rv;
            return null;
        }

        public static object? ReadPropertyValue(ControlNode node, string key, EditorType editor, int variantIndex)
        {
            if (key is "left" or "top" or "width" or "height" or "hanchor" or "vanchor")
                return ReadVariantProperty(node, key, variantIndex);

            return editor switch
            {
                EditorType.String or EditorType.Font => GetStr(node, key, ""),
                EditorType.Int => GetInt(node, key, 0),
                EditorType.Double => GetFloat(node, key, 0),
                EditorType.Bool => GetBool(node, key, false),
                EditorType.Color or EditorType.NullableColor => GetColor(node, key),
                EditorType.Dropdown => ReadDropdownValue(node, key),
                EditorType.Rect => GetRect(node, key),
                _ => null,
            };
        }

        private static object? ReadDropdownValue(ControlNode node, string key)
        {
            if (node.Properties.TryGetValue(key, out var v))
            {
                if (v is IntValue iv) return iv.Value;
                if (v is FloatValue fv) return fv.Value;
                if (v is DoubleValue dv) return dv.Value;
                if (v is StringValue sv) return sv.Value;
                if (v is StringRefValue srv) return srv.Value;
                if (v is BoolValue bv) return bv.Value;
            }
            return null;
        }

        public static double ReadVariantProperty(ControlNode node, string key, int variantIndex)
        {
            string variantKey = $"variant{variantIndex}";
            if (node.Properties.TryGetValue(variantKey, out var vo) && vo is ObjectValue ov)
            {
                if (ov.Value.TryGetValue(key, out var v))
                {
                    if (v is IntValue iv) return iv.Value;
                    if (v is FloatValue fv) return fv.Value;
                    if (v is DoubleValue dv) return dv.Value;
                }
            }
            if (node.Properties.TryGetValue(key, out var dv2))
            {
                if (dv2 is IntValue iv) return iv.Value;
                if (dv2 is FloatValue fv) return fv.Value;
                if (dv2 is DoubleValue dv) return dv.Value;
            }
            var defaults = new Dictionary<string, double>
            { ["left"] = 0, ["top"] = 0, ["width"] = 100, ["height"] = 50, ["hanchor"] = 0, ["vanchor"] = 0 };
            return defaults.GetValueOrDefault(key, 0);
        }

        public static string DetectControlType(ControlNode node)
        {
            string cs = GetStr(node, "csType", "");
            if (!string.IsNullOrEmpty(cs))
            {
                var parts = cs.Split('.');
                return parts[^1];
            }

            string java = GetStr(node, "javaType", "");
            if (java.Contains("ButtonWrapper")) return "MetaButton";
            if (java.Contains("LabelWrapper")) return "MetaLabel";
            if (java.Contains("EditTextWrapper") || java.Contains("TextFieldWrapper")) return "MetaTextField";
            if (java.Contains("ImageViewWrapper")) return "MetaImageView";
            if (java.Contains("PanelWrapper") || java.Contains("Panel")) return "MetaPanel";
            if (java.Contains("ScrollViewWrapper") || java.Contains("ScrollView")) return "MetaScrollView";
            if (java.Contains("WebViewWrapper") || java.Contains("WebView")) return "MetaWebView";
            if (java.Contains("SwitchWrapper") || java.Contains("Switch")) return "MetaSwitch";
            if (java.Contains("SeekBarWrapper") || java.Contains("SliderWrapper") || java.Contains("Slider")) return "MetaSlider";
            if (java.Contains("Stepper")) return "MetaStepper";
            if (java.Contains("SegmentedControl")) return "MetaSegmentedControl";
            if (java.Contains("Spinner") || java.Contains("Picker")) return "MetaPicker";
            if (java.Contains("DatePicker")) return "MetaDatePicker";
            if (java.Contains("ProgressBar") || java.Contains("ProgressView")) return "MetaProgressView";
            if (java.Contains("ActivityIndicator") || java.Contains("ProgressDialogWrapper")) return "MetaActivityIndicator";
            if (java.Contains("CustomView") || java.Contains("CustomViewWrapper")) return "MetaCustomView";
            return "MetaControl";
        }

        public static List<PropertyDescriptor> BuildPropertyDescriptors(ControlNode node, Platform platform, List<string> allControlNames, bool isRoot)
        {
            var props = new List<PropertyDescriptor>();
            string typeName = isRoot ? "MetaMain" : DetectControlType(node);
            string name = GetStr(node, "name", "") ?? GetStr(node, "eventName", "");
            bool isB4A = platform == Platform.B4A;
            bool isB4J = platform == Platform.B4J;

            props.Add(new PropertyDescriptor { Key = "name", DisplayName = "Name", Category = "Main", Description = "View's name", Editor = EditorType.String, IsMergeable = false, IsReadOnly = false });
            props.Add(new PropertyDescriptor { Key = "_type", DisplayName = "Type", Category = "Main", Description = "View's type", Editor = EditorType.String, IsMergeable = false, IsReadOnly = true, DefaultValue = typeName });
            props.Add(new PropertyDescriptor { Key = "eventName", DisplayName = "Event Name", Category = "Main", Description = "Sets the control's event name prefix", Editor = EditorType.String, IsMergeable = true, IsReadOnly = false });

            if (!isRoot)
            {
                var parentOptions = allControlNames.Where(n => n != name).Select(n => (n, (object)n)).ToList();
                parentOptions.Insert(0, ("", ""));
                props.Add(new PropertyDescriptor { Key = "parent", DisplayName = "Parent", Category = "Main", Description = "View's parent", Editor = EditorType.Dropdown, IsMergeable = true, IsReadOnly = false, Options = parentOptions });
            }

            props.Add(new PropertyDescriptor { Key = "hanchor", DisplayName = "Horizontal Anchor", Category = "Common Properties", Description = "Horizontal anchor mode", Editor = EditorType.Dropdown, IsMergeable = true, IsReadOnly = false, Options = new() { ("LEFT", 0), ("RIGHT", 1), ("BOTH", 2) } });
            props.Add(new PropertyDescriptor { Key = "vanchor", DisplayName = "Vertical Anchor", Category = "Common Properties", Description = "Vertical anchor mode", Editor = EditorType.Dropdown, IsMergeable = true, IsReadOnly = false, Options = new() { ("TOP", 0), ("BOTTOM", 1), ("BOTH", 2) } });
            props.Add(new PropertyDescriptor { Key = "left", DisplayName = "Left", Category = "Common Properties", Description = "Left position", Editor = EditorType.Int, IsMergeable = true, IsReadOnly = false });
            props.Add(new PropertyDescriptor { Key = "top", DisplayName = "Top", Category = "Common Properties", Description = "Top position", Editor = EditorType.Int, IsMergeable = true, IsReadOnly = false });
            props.Add(new PropertyDescriptor { Key = "width", DisplayName = "Width", Category = "Common Properties", Description = "Width", Editor = EditorType.Int, IsMergeable = true, IsReadOnly = false });
            props.Add(new PropertyDescriptor { Key = "height", DisplayName = "Height", Category = "Common Properties", Description = "Height", Editor = EditorType.Int, IsMergeable = true, IsReadOnly = false });

            if (isB4A)
                props.Add(new PropertyDescriptor { Key = "padding", DisplayName = "Padding", Category = "Common Properties", Description = "Padding (left, top, right, bottom)", Editor = EditorType.Rect, IsMergeable = true, IsReadOnly = false });
            if (isB4A || isB4J)
                props.Add(new PropertyDescriptor { Key = "enabled", DisplayName = "Enabled", Category = "Common Properties", Description = "Whether the view is enabled", Editor = EditorType.Bool, IsMergeable = true, IsReadOnly = false, DefaultValue = true });
            props.Add(new PropertyDescriptor { Key = "visible", DisplayName = "Visible", Category = "Common Properties", Description = "Whether the view is visible", Editor = EditorType.Bool, IsMergeable = true, IsReadOnly = false, DefaultValue = true });
            props.Add(new PropertyDescriptor { Key = "tag", DisplayName = "Tag", Category = "Common Properties", Description = "A string value that can be set and read from code", Editor = EditorType.String, IsMergeable = true, IsReadOnly = false });

            if (isB4J)
            {
                props.Add(new PropertyDescriptor { Key = "borderColor", DisplayName = "Border Color", Category = "Border Properties", Description = "Border color", Editor = EditorType.Color, IsMergeable = true, IsReadOnly = false, AlphaEnabled = false });
                props.Add(new PropertyDescriptor { Key = "borderWidth", DisplayName = "Border Width", Category = "Border Properties", Description = "Border width", Editor = EditorType.Double, IsMergeable = true, IsReadOnly = false, Min = 0, Max = 1000, DefaultValue = 0 });
                props.Add(new PropertyDescriptor { Key = "cornerRadius", DisplayName = "Corner Radius", Category = "Border Properties", Description = "Corner radius", Editor = EditorType.Double, IsMergeable = true, IsReadOnly = false, Min = 0, Max = 1000, DefaultValue = 0 });
            }

            AddTypeSpecificProperties(props, typeName, platform, node);
            return props;
        }

        private static void AddTypeSpecificProperties(List<PropertyDescriptor> props, string typeName, Platform platform, ControlNode node)
        {
            switch (typeName)
            {
                case "MetaButton": AddButtonProperties(props, platform); break;
                case "MetaLabel": AddLabelProperties(props); break;
                case "MetaTextField": AddTextFieldProperties(props); break;
                case "MetaTextView": AddTextViewProperties(props); break;
                case "MetaImageView": AddImageViewProperties(props); break;
                case "MetaScrollView": AddScrollViewProperties(props); break;
                case "MetaWebView": AddWebViewProperties(props); break;
                case "MetaSwitch": AddSwitchProperties(props); break;
                case "MetaSlider": AddSliderProperties(props); break;
                case "MetaProgressView": AddProgressViewProperties(props); break;
            }
        }

        private static void AddButtonProperties(List<PropertyDescriptor> props, Platform platform)
        {
            string cat = "Button Properties";
            props.Add(new PropertyDescriptor { Key = "text", DisplayName = "Text", Category = cat, Description = "Button text", Editor = EditorType.String, IsMergeable = true, IsReadOnly = false });
            props.Add(new PropertyDescriptor { Key = "fontAwesome", DisplayName = "Font Awesome", Category = cat, Description = "FontAwesome icon name", Editor = EditorType.String, IsMergeable = true, IsReadOnly = false });
            props.Add(new PropertyDescriptor { Key = "materialIcons", DisplayName = "Material Icons", Category = cat, Description = "Material icon name", Editor = EditorType.String, IsMergeable = true, IsReadOnly = false });
            props.Add(new PropertyDescriptor { Key = "textColor", DisplayName = "Text Color", Category = cat, Description = "Text color", Editor = EditorType.Color, IsMergeable = true, IsReadOnly = false, AlphaEnabled = false });
            props.Add(new PropertyDescriptor { Key = "pressedTextColor", DisplayName = "Pressed Text Color", Category = cat, Description = "Text color when pressed", Editor = EditorType.NullableColor, IsMergeable = true, IsReadOnly = false, AlphaEnabled = false });
            props.Add(new PropertyDescriptor { Key = "backgroundImage", DisplayName = "Background Image", Category = cat, Description = "Background image file", Editor = EditorType.String, IsMergeable = true, IsReadOnly = false });
            props.Add(new PropertyDescriptor { Key = "pressedBackgroundImage", DisplayName = "Pressed Background Image", Category = cat, Description = "Background image when pressed", Editor = EditorType.String, IsMergeable = true, IsReadOnly = false });
        }

        private static void AddLabelProperties(List<PropertyDescriptor> props)
        {
            string cat = "Label Properties";
            props.Add(new PropertyDescriptor { Key = "text", DisplayName = "Text", Category = cat, Description = "Label text", Editor = EditorType.String, IsMergeable = true, IsReadOnly = false });
            props.Add(new PropertyDescriptor { Key = "fontAwesome", DisplayName = "Font Awesome", Category = cat, Description = "FontAwesome icon name", Editor = EditorType.String, IsMergeable = true, IsReadOnly = false });
            props.Add(new PropertyDescriptor { Key = "materialIcons", DisplayName = "Material Icons", Category = cat, Description = "Material icon name", Editor = EditorType.String, IsMergeable = true, IsReadOnly = false });
            props.Add(new PropertyDescriptor { Key = "textColor", DisplayName = "Text Color", Category = cat, Description = "Text color", Editor = EditorType.Color, IsMergeable = true, IsReadOnly = false, AlphaEnabled = false });
            props.Add(new PropertyDescriptor { Key = "multiline", DisplayName = "Multiline", Category = cat, Description = "Whether text wraps to multiple lines", Editor = EditorType.Bool, IsMergeable = true, IsReadOnly = false, DefaultValue = false });
            props.Add(new PropertyDescriptor { Key = "adjustFontSizeToFit", DisplayName = "Adjust Font Size To Fit", Category = cat, Description = "Automatically adjust font size to fit", Editor = EditorType.Bool, IsMergeable = true, IsReadOnly = false, DefaultValue = false });
            AddTextAlignmentDropdown(props, cat);
        }

        private static void AddTextFieldProperties(List<PropertyDescriptor> props)
        {
            string cat = "Text Properties";
            props.Add(new PropertyDescriptor { Key = "text", DisplayName = "Text", Category = cat, Description = "Text field content", Editor = EditorType.String, IsMergeable = true, IsReadOnly = false });
            props.Add(new PropertyDescriptor { Key = "textColor", DisplayName = "Text Color", Category = cat, Description = "Text color", Editor = EditorType.Color, IsMergeable = true, IsReadOnly = false, AlphaEnabled = false });
            AddTextAlignmentDropdown(props, cat);
            props.Add(new PropertyDescriptor { Key = "hintText", DisplayName = "Hint Text", Category = cat, Description = "Placeholder text", Editor = EditorType.String, IsMergeable = true, IsReadOnly = false });
            props.Add(new PropertyDescriptor { Key = "passwordMode", DisplayName = "Password Mode", Category = cat, Description = "Hide typed text", Editor = EditorType.Bool, IsMergeable = true, IsReadOnly = false, DefaultValue = false });
        }

        private static void AddTextViewProperties(List<PropertyDescriptor> props)
        {
            string cat = "Text Properties";
            props.Add(new PropertyDescriptor { Key = "text", DisplayName = "Text", Category = cat, Description = "Text content", Editor = EditorType.String, IsMergeable = true, IsReadOnly = false });
            props.Add(new PropertyDescriptor { Key = "textColor", DisplayName = "Text Color", Category = cat, Description = "Text color", Editor = EditorType.Color, IsMergeable = true, IsReadOnly = false, AlphaEnabled = false });
            AddTextAlignmentDropdown(props, cat);
            props.Add(new PropertyDescriptor { Key = "editable", DisplayName = "Editable", Category = cat, Description = "Whether the text view is editable", Editor = EditorType.Bool, IsMergeable = true, IsReadOnly = false, DefaultValue = false });
        }

        private static void AddImageViewProperties(List<PropertyDescriptor> props)
        {
            string cat = "ImageView Properties";
            props.Add(new PropertyDescriptor { Key = "imageFile", DisplayName = "Image File", Category = cat, Description = "Image file name", Editor = EditorType.String, IsMergeable = true, IsReadOnly = false });
            props.Add(new PropertyDescriptor { Key = "contentMode", DisplayName = "Content Mode", Category = cat, Description = "How image fills the view", Editor = EditorType.Dropdown, IsMergeable = true, IsReadOnly = false, Options = new() { ("FILL", 0), ("FIT", 1), ("CENTER", 4), ("TOPLEFT", 9) } });
        }

        private static void AddScrollViewProperties(List<PropertyDescriptor> props)
        {
            string cat = "ScrollView Properties";
            props.Add(new PropertyDescriptor { Key = "contentWidth", DisplayName = "Content Width", Category = cat, Description = "Scroll content width", Editor = EditorType.Int, IsMergeable = true, IsReadOnly = false });
            props.Add(new PropertyDescriptor { Key = "contentHeight", DisplayName = "Content Height", Category = cat, Description = "Scroll content height", Editor = EditorType.Int, IsMergeable = true, IsReadOnly = false });
            props.Add(new PropertyDescriptor { Key = "pagingEnabled", DisplayName = "Paging Enabled", Category = cat, Description = "Enable paging", Editor = EditorType.Bool, IsMergeable = true, IsReadOnly = false, DefaultValue = false });
            props.Add(new PropertyDescriptor { Key = "bounces", DisplayName = "Bounces", Category = cat, Description = "Enable bounce effect", Editor = EditorType.Bool, IsMergeable = true, IsReadOnly = false, DefaultValue = true });
            props.Add(new PropertyDescriptor { Key = "showsVerticalIndicator", DisplayName = "Shows Vertical Indicator", Category = cat, Description = "Show vertical scroll indicator", Editor = EditorType.Bool, IsMergeable = true, IsReadOnly = false, DefaultValue = true });
            props.Add(new PropertyDescriptor { Key = "showsHorizontalIndicator", DisplayName = "Shows Horizontal Indicator", Category = cat, Description = "Show horizontal scroll indicator", Editor = EditorType.Bool, IsMergeable = true, IsReadOnly = false, DefaultValue = true });
        }

        private static void AddWebViewProperties(List<PropertyDescriptor> props)
        {
            props.Add(new PropertyDescriptor { Key = "suppressRendering", DisplayName = "Suppress Rendering", Category = "WebView Properties", Description = "Suppress rendering on designer", Editor = EditorType.Bool, IsMergeable = true, IsReadOnly = false, DefaultValue = false });
        }

        private static void AddSwitchProperties(List<PropertyDescriptor> props)
        {
            string cat = "Switch Properties";
            props.Add(new PropertyDescriptor { Key = "value", DisplayName = "Value", Category = cat, Description = "Switch state", Editor = EditorType.Bool, IsMergeable = true, IsReadOnly = false, DefaultValue = false });
            props.Add(new PropertyDescriptor { Key = "onColor", DisplayName = "On Color", Category = cat, Description = "Color when switch is on", Editor = EditorType.NullableColor, IsMergeable = true, IsReadOnly = false, AlphaEnabled = false });
            props.Add(new PropertyDescriptor { Key = "thumbColor", DisplayName = "Thumb Color", Category = cat, Description = "Thumb color", Editor = EditorType.NullableColor, IsMergeable = true, IsReadOnly = false, AlphaEnabled = false });
        }

        private static void AddSliderProperties(List<PropertyDescriptor> props)
        {
            string cat = "Slider Properties";
            props.Add(new PropertyDescriptor { Key = "value", DisplayName = "Value", Category = cat, Description = "Slider value", Editor = EditorType.Double, IsMergeable = true, IsReadOnly = false, DefaultValue = 50 });
            props.Add(new PropertyDescriptor { Key = "minimumValue", DisplayName = "Minimum Value", Category = cat, Description = "Minimum value", Editor = EditorType.Double, IsMergeable = true, IsReadOnly = false, DefaultValue = 0 });
            props.Add(new PropertyDescriptor { Key = "maximumValue", DisplayName = "Maximum Value", Category = cat, Description = "Maximum value", Editor = EditorType.Double, IsMergeable = true, IsReadOnly = false, DefaultValue = 100 });
            props.Add(new PropertyDescriptor { Key = "minimumTrackTintColor", DisplayName = "Min Track Tint Color", Category = cat, Description = "Minimum track tint color", Editor = EditorType.NullableColor, IsMergeable = true, IsReadOnly = false, AlphaEnabled = false });
            props.Add(new PropertyDescriptor { Key = "continuous", DisplayName = "Continuous", Category = cat, Description = "Fire value changed events continuously", Editor = EditorType.Bool, IsMergeable = true, IsReadOnly = false, DefaultValue = true });
        }

        private static void AddProgressViewProperties(List<PropertyDescriptor> props)
        {
            props.Add(new PropertyDescriptor { Key = "progressColor", DisplayName = "Progress Color", Category = "ProgressView Properties", Description = "Progress indicator color", Editor = EditorType.NullableColor, IsMergeable = true, IsReadOnly = false, AlphaEnabled = false });
        }

        private static void AddTextAlignmentDropdown(List<PropertyDescriptor> props, string category)
        {
            props.Add(new PropertyDescriptor { Key = "textAlignment", DisplayName = "Text Alignment", Category = category, Description = "Text alignment", Editor = EditorType.Dropdown, IsMergeable = true, IsReadOnly = false, Options = new() { ("LEFT", 0), ("CENTER", 1), ("RIGHT", 2) } });
        }

        public static void ApplyAnchorLabels(List<PropertyDescriptor> props, double hanchor, double vanchor)
        {
            foreach (var p in props)
            {
                if (p.Key == "left")
                {
                    p.DisplayName = hanchor == AnchorRight ? "Right Edge Distance" : "Left";
                    p.IsReadOnly = hanchor < 0;
                }
                if (p.Key == "width")
                {
                    p.DisplayName = hanchor == AnchorBoth ? "Right Edge Distance" : "Width";
                    p.IsReadOnly = hanchor < 0;
                }
                if (p.Key == "top")
                {
                    p.DisplayName = vanchor == AnchorRight ? "Bottom Edge Distance" : "Top";
                    p.IsReadOnly = vanchor < 0;
                }
                if (p.Key == "height")
                {
                    p.DisplayName = vanchor == AnchorBoth ? "Bottom Edge Distance" : "Height";
                    p.IsReadOnly = vanchor < 0;
                }
            }
        }

        public static List<PropertyData> BuildPropertyDataForControl(ControlNode node, Platform platform, List<string> allControlNames, bool isRoot, int variantIndex)
        {
            var descriptors = BuildPropertyDescriptors(node, platform, allControlNames, isRoot);
            double hanchor = ReadVariantProperty(node, "hanchor", variantIndex);
            double vanchor = ReadVariantProperty(node, "vanchor", variantIndex);
            ApplyAnchorLabels(descriptors, hanchor, vanchor);

            return descriptors.Select(d => new PropertyData
            {
                Key = d.Key,
                DisplayName = d.DisplayName,
                Category = d.Category,
                Description = d.Description,
                Editor = d.Editor.ToString(),
                IsMergeable = d.IsMergeable,
                IsReadOnly = d.IsReadOnly,
                Value = d.Key == "_type" ? d.DefaultValue : ReadPropertyValue(node, d.Key, d.Editor, variantIndex),
                Options = d.Options,
                Min = d.Min,
                Max = d.Max,
                Step = d.Step,
                AlphaEnabled = d.AlphaEnabled,
            }).ToList();
        }

        public static List<string> CollectControlNames(ControlNode node)
        {
            var names = new List<string>();
            string name = GetStr(node, "name", "") ?? GetStr(node, "eventName", "");
            if (!string.IsNullOrEmpty(name)) names.Add(name);
            foreach (var child in node.Children)
                names.AddRange(CollectControlNames(child));
            return names;
        }

        public static ControlNode? FindControlByName(ControlNode root, string name)
        {
            string n = GetStr(root, "name", "") ?? GetStr(root, "eventName", "");
            if (n == name) return root;
            foreach (var child in root.Children)
            {
                var found = FindControlByName(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
