namespace PsdLayoutTool2
{
    using System.Collections.Generic;
    using PhotoshopFile;
    using UnityEngine;

    /// <summary>
    /// Builds suffix-aware metadata for a PSD layer tree.
    /// </summary>
    internal sealed class LayerImportInfoBuilder
    {
        private readonly bool enableAutoAnchorByName;
        private readonly LayerSuffixProcessor suffixProcessor;

        public LayerImportInfoBuilder(bool enableAutoAnchorByName)
        {
            this.enableAutoAnchorByName = enableAutoAnchorByName;
            suffixProcessor = LayerSuffixProcessorRegistry.Active;
        }

        public Dictionary<Layer, LayerImportInfo> Build(List<Layer> tree)
        {
            Dictionary<Layer, LayerImportInfo> result = new Dictionary<Layer, LayerImportInfo>();
            if (tree == null)
            {
                return result;
            }

            foreach (Layer layer in tree)
            {
                BuildLayer(layer, null, true, result);
            }

            return result;
        }

        private void BuildLayer(
            Layer layer,
            LayerImportInfo parent,
            bool parentVisible,
            Dictionary<Layer, LayerImportInfo> result)
        {
            LayerImportInfo info = new LayerImportInfo(layer)
            {
                Parent = parent,
                EffectiveVisible = parentVisible && layer.Visible,
                IsFolderLike = layer.Children.Count > 0 || layer.Rect.width == 0
            };

            suffixProcessor.Apply(info, parent, enableAutoAnchorByName);
            result[layer] = info;

            foreach (Layer child in layer.Children)
            {
                BuildLayer(child, info, info.EffectiveVisible, result);
            }

            Rect layoutRect;
            info.HasLayoutRect = TryResolveLayerLayoutRect(info, result, out layoutRect);
            info.LayoutRect = layoutRect;
        }

        private static bool TryResolveLayerLayoutRect(
            LayerImportInfo info,
            Dictionary<Layer, LayerImportInfo> infoMap,
            out Rect layoutRect)
        {
            layoutRect = default(Rect);
            if (info == null || info.Layer == null)
            {
                return false;
            }

            if (!info.IsFolderLike)
            {
                if (info.Layer.Rect.width > 0f && info.Layer.Rect.height > 0f)
                {
                    layoutRect = info.Layer.Rect;
                    return true;
                }

                return false;
            }

            bool hasBounds = false;
            Rect combinedRect = default(Rect);
            foreach (Layer child in info.Layer.Children)
            {
                LayerImportInfo childInfo;
                if (!infoMap.TryGetValue(child, out childInfo) || childInfo == null || !childInfo.EffectiveVisible || !childInfo.HasLayoutRect)
                {
                    continue;
                }

                combinedRect = hasBounds ? CombineRects(combinedRect, childInfo.LayoutRect) : childInfo.LayoutRect;
                hasBounds = true;
            }

            if (hasBounds)
            {
                layoutRect = combinedRect;
                return true;
            }

            if (info.Layer.Rect.width > 0f && info.Layer.Rect.height > 0f)
            {
                layoutRect = info.Layer.Rect;
                return true;
            }

            return false;
        }

        private static Rect CombineRects(Rect first, Rect second)
        {
            return Rect.MinMaxRect(
                Mathf.Min(first.xMin, second.xMin),
                Mathf.Min(first.yMin, second.yMin),
                Mathf.Max(first.xMax, second.xMax),
                Mathf.Max(first.yMax, second.yMax));
        }
    }
}
