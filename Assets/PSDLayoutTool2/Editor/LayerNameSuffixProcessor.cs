namespace PsdLayoutTool2
{
    using System;
    using System.Collections.Generic;
    using UnityEngine;

    /// <summary>
    /// Parsed source layer name split into its visible name and pipe-delimited tags.
    /// </summary>
    internal sealed class LayerNameParts
    {
        private readonly List<string> suffixes;

        internal LayerNameParts(string originalName, string baseName, List<string> parsedSuffixes)
        {
            OriginalName = originalName;
            BaseName = baseName;
            suffixes = parsedSuffixes;
        }

        public string OriginalName { get; private set; }

        public string BaseName { get; private set; }

        public bool HasTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return false;
            }

            for (int i = 0; i < suffixes.Count; i++)
            {
                if (string.Equals(suffixes[i], tag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryGetValue(string key, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            string prefix = key + "=";
            for (int i = 0; i < suffixes.Count; i++)
            {
                string suffix = suffixes[i];
                if (suffix.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    value = suffix.Substring(prefix.Length);
                    return true;
                }
            }

            return false;
        }

        public string WithoutTags(params string[] tags)
        {
            if (tags == null || tags.Length == 0)
            {
                return OriginalName;
            }

            string result = BaseName;
            for (int i = 0; i < suffixes.Count; i++)
            {
                if (!IsTag(suffixes[i], tags))
                {
                    result += "|" + suffixes[i];
                }
            }

            return result;
        }

        private static bool IsTag(string suffix, string[] tags)
        {
            for (int i = 0; i < tags.Length; i++)
            {
                if (string.Equals(suffix, tags[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Splits a PSD layer name into its base name and pipe-delimited suffix tokens.
    /// </summary>
    internal sealed class LayerNameSuffixParser
    {
        private static readonly LayerNameSuffixParser DefaultParser = new LayerNameSuffixParser();

        public static LayerNameSuffixParser Default
        {
            get { return DefaultParser; }
        }

        public LayerNameParts Parse(string name)
        {
            string originalName = name ?? string.Empty;
            string[] tokens = originalName.Split(new[] { '|' }, StringSplitOptions.None);
            List<string> parsedSuffixes = new List<string>();
            for (int i = 1; i < tokens.Length; i++)
            {
                parsedSuffixes.Add(tokens[i]);
            }

            return new LayerNameParts(originalName, tokens.Length > 0 ? tokens[0] : string.Empty, parsedSuffixes);
        }
    }

    /// <summary>
    /// Provides the context required by one name-suffix handler.
    /// </summary>
    internal sealed class LayerSuffixContext
    {
        public LayerSuffixContext(LayerImportInfo info, LayerImportInfo parent, bool enableAutoAnchorByName)
        {
            Info = info;
            Parent = parent;
            EnableAutoAnchorByName = enableAutoAnchorByName;
        }

        public LayerImportInfo Info { get; private set; }

        public LayerImportInfo Parent { get; private set; }

        public bool EnableAutoAnchorByName { get; private set; }
    }

    /// <summary>
    /// Handles one independent layer-name suffix concern.
    /// </summary>
    internal interface ILayerSuffixHandler
    {
        void Apply(LayerSuffixContext context);
    }

    /// <summary>
    /// Applies all supported pipe-suffix semantics to a layer's import metadata.
    /// </summary>
    internal sealed class LayerSuffixProcessor
    {
        private readonly ILayerSuffixHandler[] handlers;

        public LayerSuffixProcessor()
            : this(
                new ButtonSuffixHandler(),
                new AnimationSuffixHandler(),
                new ButtonChildRoleSuffixHandler(),
                new AnchorSuffixHandler())
        {
        }

        public LayerSuffixProcessor(params ILayerSuffixHandler[] handlers)
        {
            this.handlers = handlers ?? new ILayerSuffixHandler[0];
        }

        public void Apply(LayerImportInfo info, LayerImportInfo parent, bool enableAutoAnchorByName)
        {
            LayerSuffixContext context = new LayerSuffixContext(info, parent, enableAutoAnchorByName);
            for (int i = 0; i < handlers.Length; i++)
            {
                handlers[i].Apply(context);
            }
        }
    }

    /// <summary>
    /// Holds the active suffix processor for import metadata construction.
    /// </summary>
    internal static class LayerSuffixProcessorRegistry
    {
        static LayerSuffixProcessorRegistry()
        {
            ResetDefault();
        }

        public static LayerSuffixProcessor Active { get; set; }

        public static void ResetDefault()
        {
            Active = new LayerSuffixProcessor();
        }
    }

    internal sealed class ButtonSuffixHandler : ILayerSuffixHandler
    {
        public void Apply(LayerSuffixContext context)
        {
            context.Info.IsButtonGroup = context.Info.IsFolderLike && context.Info.NameParts.HasTag("Button");
        }
    }

    internal sealed class AnimationSuffixHandler : ILayerSuffixHandler
    {
        public void Apply(LayerSuffixContext context)
        {
            context.Info.IsAnimationGroup = context.Info.IsFolderLike && context.Info.NameParts.HasTag("Animation");
            context.Info.AnimationFps = GetAnimationFps(context.Info.NameParts);
        }

        private static float GetAnimationFps(LayerNameParts nameParts)
        {
            float fps = 30f;
            string fpsValue;
            if (nameParts == null || !nameParts.TryGetValue("FPS", out fpsValue))
            {
                return fps;
            }

            float parsedFps;
            if (float.TryParse(fpsValue, out parsedFps))
            {
                return parsedFps;
            }

            Debug.LogError(string.Format("Unable to parse FPS: \"FPS={0}\"", fpsValue));
            return fps;
        }
    }

    internal sealed class ButtonChildRoleSuffixHandler : ILayerSuffixHandler
    {
        public void Apply(LayerSuffixContext context)
        {
            if (context.Parent == null || !context.Parent.IsButtonGroup)
            {
                context.Info.ButtonRole = ButtonChildRole.None;
                return;
            }

            LayerNameParts nameParts = context.Info.NameParts;
            if (nameParts.HasTag("Disabled"))
            {
                context.Info.ButtonRole = ButtonChildRole.Disabled;
            }
            else if (nameParts.HasTag("Highlighted"))
            {
                context.Info.ButtonRole = ButtonChildRole.Highlighted;
            }
            else if (nameParts.HasTag("Pressed"))
            {
                context.Info.ButtonRole = ButtonChildRole.Pressed;
            }
            else if (nameParts.HasTag("Default") || nameParts.HasTag("Enabled") || nameParts.HasTag("Normal") || nameParts.HasTag("Up"))
            {
                context.Info.ButtonRole = ButtonChildRole.Default;
            }
            else if (nameParts.HasTag("Text") && !context.Info.Layer.IsTextLayer)
            {
                context.Info.ButtonRole = ButtonChildRole.TextImage;
            }
            else
            {
                context.Info.ButtonRole = ButtonChildRole.None;
            }
        }
    }

    internal sealed class AnchorSuffixHandler : ILayerSuffixHandler
    {
        public void Apply(LayerSuffixContext context)
        {
            context.Info.ExplicitAnchorPreset = ParseAnchorPreset(context.Info.NameParts.BaseName);
            context.Info.AnchorPreset = ResolveAnchorPreset(context.Info, context.EnableAutoAnchorByName);
        }

        private static AnchorNamePreset ResolveAnchorPreset(LayerImportInfo info, bool enableAutoAnchorByName)
        {
            if (!enableAutoAnchorByName || info == null)
            {
                return AnchorNamePreset.None;
            }

            if (info.ExplicitAnchorPreset != AnchorNamePreset.None)
            {
                return info.ExplicitAnchorPreset;
            }

            if (info.Parent != null && info.Parent.IsFolderLike && info.Parent.AnchorPreset != AnchorNamePreset.None)
            {
                return info.Parent.AnchorPreset;
            }

            return AnchorNamePreset.None;
        }

        private static AnchorNamePreset ParseAnchorPreset(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return AnchorNamePreset.None;
            }

            string trimmedName = name.TrimStart();
            if (trimmedName.StartsWith("全局", StringComparison.OrdinalIgnoreCase)) return AnchorNamePreset.Global;
            if (trimmedName.StartsWith("左上", StringComparison.OrdinalIgnoreCase)) return AnchorNamePreset.TopLeft;
            if (trimmedName.StartsWith("左下", StringComparison.OrdinalIgnoreCase)) return AnchorNamePreset.BottomLeft;
            if (trimmedName.StartsWith("右上", StringComparison.OrdinalIgnoreCase)) return AnchorNamePreset.TopRight;
            if (trimmedName.StartsWith("右下", StringComparison.OrdinalIgnoreCase)) return AnchorNamePreset.BottomRight;
            if (trimmedName.StartsWith("中间", StringComparison.OrdinalIgnoreCase)) return AnchorNamePreset.Center;
            if (trimmedName.StartsWith("左中", StringComparison.OrdinalIgnoreCase)) return AnchorNamePreset.LeftMiddle;
            if (trimmedName.StartsWith("右中", StringComparison.OrdinalIgnoreCase)) return AnchorNamePreset.RightMiddle;
            if (trimmedName.StartsWith("上中", StringComparison.OrdinalIgnoreCase)) return AnchorNamePreset.TopMiddle;
            if (trimmedName.StartsWith("下中", StringComparison.OrdinalIgnoreCase)) return AnchorNamePreset.BottomMiddle;
            if (trimmedName.StartsWith("上", StringComparison.OrdinalIgnoreCase)) return AnchorNamePreset.TopMiddle;
            if (trimmedName.StartsWith("下", StringComparison.OrdinalIgnoreCase)) return AnchorNamePreset.BottomMiddle;
            if (trimmedName.StartsWith("左", StringComparison.OrdinalIgnoreCase)) return AnchorNamePreset.LeftMiddle;
            if (trimmedName.StartsWith("右", StringComparison.OrdinalIgnoreCase)) return AnchorNamePreset.RightMiddle;
            return AnchorNamePreset.None;
        }
    }

    /// <summary>
    /// Centralizes layer-name cleanup for generated objects and assets.
    /// </summary>
    internal static class LayerNameResolver
    {
        public static string GetAnimationBaseName(LayerNameParts nameParts)
        {
            return string.IsNullOrWhiteSpace(nameParts.BaseName) ? "Animation" : nameParts.BaseName.Trim();
        }

        public static string GetButtonGroupBaseName(LayerNameParts nameParts)
        {
            return nameParts.WithoutTags("Button");
        }

        public static string GetButtonChildBaseName(LayerImportInfo info)
        {
            string name = info.NameParts.WithoutTags("Disabled", "Highlighted", "Pressed", "Default", "Enabled", "Normal", "Up");
            return info.Layer != null && !info.Layer.IsTextLayer
                ? LayerNameSuffixParser.Default.Parse(name).WithoutTags("Text")
                : name;
        }
    }
}
