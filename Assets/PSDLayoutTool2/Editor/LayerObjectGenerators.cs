namespace PsdLayoutTool2
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using PhotoshopFile;
    using UnityEngine;
    using UnityEngine.UI;

    /// <summary>
    /// Mutable import state and services shared by layer object generators.
    /// </summary>
    internal sealed class PsdLayerGenerationContext
    {
        public GameObject ParentGameObject { get; set; }

        public Vector2 CanvasSize { get; set; }

        public float PixelsToUnits { get; set; }

        public float CurrentDepth { get; set; }

        public float DepthStep { get; set; }

        public int CurrentSortingOrder { get; set; }

        public bool UseTargetCanvasCoordinates { get; set; }

        public float TargetCanvasUniformScale { get; set; }

        public Func<Layer, LayerImportInfo> LayerInfoProvider { get; set; }

        public Func<Layer, string> RuntimeNameProvider { get; set; }

        public Func<Layer, Sprite> SpriteProvider { get; set; }

        public Func<Layer, Font> FontProvider { get; set; }

        public Action<RectTransform, Layer, AnchorNamePreset> UILayoutApplier { get; set; }

        public Func<AnchorNamePreset, bool> GlobalAnchorPredicate { get; set; }
    }

    internal interface IImageLayerGenerator
    {
        Component Generate(Layer layer, PsdLayerGenerationContext context);
    }

    internal interface ITextLayerGenerator
    {
        Component Generate(Layer layer, PsdLayerGenerationContext context);
    }

    /// <summary>
    /// Holds the active generators so individual implementations can be replaced independently.
    /// </summary>
    internal static class LayerObjectGeneratorRegistry
    {
        static LayerObjectGeneratorRegistry()
        {
            ResetDefaults();
        }

        public static IImageLayerGenerator UIImageGenerator { get; set; }

        public static ITextLayerGenerator UITextGenerator { get; set; }

        public static IImageLayerGenerator SpriteRendererGenerator { get; set; }

        public static ITextLayerGenerator TextMeshGenerator { get; set; }

        public static void ResetDefaults()
        {
            UIImageGenerator = new UnityUIImageLayerGenerator();
            UITextGenerator = new UnityUITextLayerGenerator();
            SpriteRendererGenerator = new SpriteRendererLayerGenerator();
            TextMeshGenerator = new TextMeshLayerGenerator();
        }
    }

    internal sealed class UnityUIImageLayerGenerator : IImageLayerGenerator
    {
        public Component Generate(Layer layer, PsdLayerGenerationContext context)
        {
            LayerImportInfo info = context.LayerInfoProvider(layer);
            AnchorNamePreset preset = info != null ? info.AnchorPreset : AnchorNamePreset.None;

            GameObject uiObject = new GameObject(context.RuntimeNameProvider(layer), typeof(RectTransform));
            uiObject.transform.SetParent(context.ParentGameObject.transform, false);

            RectTransform uiTransform = uiObject.GetComponent<RectTransform>();
            context.UILayoutApplier(uiTransform, layer, preset);

            Image image = uiObject.AddComponent<Image>();
            image.sprite = context.SpriteProvider(layer);
            UIImageLayoutBehavior.Apply(image, preset, context.GlobalAnchorPredicate);
            return image;
        }
    }

    internal sealed class UnityUITextLayerGenerator : ITextLayerGenerator
    {
        public Component Generate(Layer layer, PsdLayerGenerationContext context)
        {
            LayerImportInfo info = context.LayerInfoProvider(layer);
            AnchorNamePreset preset = info != null ? info.AnchorPreset : AnchorNamePreset.None;
            Color color = LayerColorUtility.ApplyOpacity(layer.FillColor, layer);

            GameObject uiObject = new GameObject(context.RuntimeNameProvider(layer), typeof(RectTransform));
            uiObject.transform.SetParent(context.ParentGameObject.transform, false);

            RectTransform uiTransform = uiObject.GetComponent<RectTransform>();
            context.UILayoutApplier(uiTransform, layer, preset);

            Text text = uiObject.AddComponent<Text>();
            text.text = layer.Text;
            text.font = context.FontProvider(layer);

            float fontSize = context.UseTargetCanvasCoordinates
                ? layer.FontSize * context.TargetCanvasUniformScale
                : layer.FontSize / context.PixelsToUnits;
            float ceiling = Mathf.Ceil(fontSize);
            if (fontSize > 0f && fontSize < ceiling)
            {
                text.fontSize = (int)ceiling;
                if (!context.GlobalAnchorPredicate(preset))
                {
                    float scaleFactor = ceiling / fontSize;
                    text.rectTransform.sizeDelta *= scaleFactor;
                    text.rectTransform.localScale /= scaleFactor;
                }
            }
            else
            {
                text.fontSize = Mathf.Max(1, (int)ceiling);
            }

            text.color = color;
            text.alignment = GetTextAnchor(layer.Justification);
            return text;
        }

        private static TextAnchor GetTextAnchor(TextJustification justification)
        {
            switch (justification)
            {
                case TextJustification.Left:
                    return TextAnchor.MiddleLeft;
                case TextJustification.Right:
                    return TextAnchor.MiddleRight;
                default:
                    return TextAnchor.MiddleCenter;
            }
        }
    }

    internal static class UIImageLayoutBehavior
    {
        public static void Apply(
            Image image,
            AnchorNamePreset preset,
            Func<AnchorNamePreset, bool> globalAnchorPredicate)
        {
            if (image == null)
            {
                return;
            }

            image.preserveAspect = true;
            AspectRatioFitter fitter = image.GetComponent<AspectRatioFitter>();
            if (!globalAnchorPredicate(preset) || image.sprite == null || image.sprite.rect.height <= 0f)
            {
                if (fitter != null)
                {
                    UnityEngine.Object.DestroyImmediate(fitter);
                }

                return;
            }

            if (fitter == null)
            {
                fitter = image.gameObject.AddComponent<AspectRatioFitter>();
            }

            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = image.sprite.rect.width / image.sprite.rect.height;
        }
    }

    internal sealed class SpriteRendererLayerGenerator : IImageLayerGenerator
    {
        public Component Generate(Layer layer, PsdLayerGenerationContext context)
        {
            Vector3 position = GetScenePosition(layer, context);
            GameObject gameObject = new GameObject(context.RuntimeNameProvider(layer));
            gameObject.transform.position = position;
            gameObject.transform.parent = context.ParentGameObject.transform;
            context.CurrentDepth -= context.DepthStep;

            SpriteRenderer renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = context.SpriteProvider(layer);
            renderer.sortingOrder = context.CurrentSortingOrder++;
            return renderer;
        }

        private static Vector3 GetScenePosition(Layer layer, PsdLayerGenerationContext context)
        {
            float x = layer.Rect.x / context.PixelsToUnits;
            float y = (context.CanvasSize.y - layer.Rect.y) / context.PixelsToUnits;
            float width = layer.Rect.width / context.PixelsToUnits;
            float height = layer.Rect.height / context.PixelsToUnits;
            return new Vector3(x + (width / 2f), y - (height / 2f), context.CurrentDepth);
        }
    }

    internal sealed class TextMeshLayerGenerator : ITextLayerGenerator
    {
        public Component Generate(Layer layer, PsdLayerGenerationContext context)
        {
            float x = layer.Rect.x / context.PixelsToUnits;
            float y = (context.CanvasSize.y - layer.Rect.y) / context.PixelsToUnits;
            float width = layer.Rect.width / context.PixelsToUnits;
            float height = layer.Rect.height / context.PixelsToUnits;

            GameObject gameObject = new GameObject(context.RuntimeNameProvider(layer));
            gameObject.transform.position = new Vector3(x + (width / 2f), y - (height / 2f), context.CurrentDepth);
            gameObject.transform.parent = context.ParentGameObject.transform;
            context.CurrentDepth -= context.DepthStep;

            Font font = context.FontProvider(layer);
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.material = font.material;
            renderer.sortingOrder = context.CurrentSortingOrder++;

            TextMesh text = gameObject.AddComponent<TextMesh>();
            text.text = layer.Text;
            text.font = font;
            text.fontSize = 0;
            text.characterSize = layer.FontSize / context.PixelsToUnits;
            text.color = LayerColorUtility.ApplyOpacity(layer.FillColor, layer);
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = GetTextAlignment(layer.Justification);
            return text;
        }

        private static TextAlignment GetTextAlignment(TextJustification justification)
        {
            switch (justification)
            {
                case TextJustification.Left:
                    return TextAlignment.Left;
                case TextJustification.Right:
                    return TextAlignment.Right;
                default:
                    return TextAlignment.Center;
            }
        }
    }

    internal static class LayerFontResolver
    {
        public static Font Resolve(Layer layer)
        {
            List<string> fontCandidates = new List<string>();
            if (!string.IsNullOrEmpty(layer.FontName))
            {
                fontCandidates.Add(layer.FontName.Trim());
            }

            fontCandidates.Add("Microsoft YaHei");
            fontCandidates.Add("SimHei");
            fontCandidates.Add("SimSun");
            fontCandidates.Add("PingFang SC");
            fontCandidates.Add("Heiti SC");
            fontCandidates.Add("Noto Sans CJK SC");
            fontCandidates.Add("Arial Unicode MS");
            fontCandidates.Add("Arial");

            foreach (string fontName in fontCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(fontName))
                {
                    continue;
                }

                try
                {
                    Font font = Font.CreateDynamicFontFromOSFont(fontName, 16);
                    if (font != null)
                    {
                        return font;
                    }
                }
                catch
                {
                    // Continue with the next available font.
                }
            }

            return Resources.GetBuiltinResource<Font>("Arial.ttf");
        }
    }

    internal static class LayerColorUtility
    {
        public static Color ApplyOpacity(Color color, Layer layer)
        {
            float layerOpacity = layer != null ? layer.Opacity / (float)byte.MaxValue : 1f;
            color.a = Mathf.Clamp01(color.a) * layerOpacity;
            return color;
        }
    }
}
