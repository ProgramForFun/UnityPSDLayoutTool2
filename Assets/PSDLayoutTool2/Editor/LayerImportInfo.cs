namespace PsdLayoutTool2
{
    using PhotoshopFile;
    using UnityEngine;

    internal enum ButtonChildRole
    {
        None,
        Default,
        Pressed,
        Highlighted,
        Disabled,
        TextImage
    }

    internal enum AnchorNamePreset
    {
        None,
        Global,
        TopLeft,
        BottomLeft,
        TopRight,
        BottomRight,
        Center,
        LeftMiddle,
        RightMiddle,
        TopMiddle,
        BottomMiddle
    }

    internal struct UiLayoutContext
    {
        public Rect PsdReferenceRect { get; set; }

        public Vector2 LocalRectSize { get; set; }

        public Rect LocalDisplayRect { get; set; }
    }

    internal sealed class LayerImportInfo
    {
        public LayerImportInfo(Layer layer)
        {
            Layer = layer;
            NameParts = LayerNameSuffixParser.Default.Parse(layer != null ? layer.Name : string.Empty);
        }

        public Layer Layer { get; private set; }

        public LayerNameParts NameParts { get; set; }

        public LayerImportInfo Parent { get; set; }

        public bool EffectiveVisible { get; set; }

        public bool IsFolderLike { get; set; }

        public bool IsButtonGroup { get; set; }

        public bool IsAnimationGroup { get; set; }

        public ButtonChildRole ButtonRole { get; set; }

        public string UniqueSelfName { get; set; }

        public string UniqueTextureName { get; set; }

        public float AnimationFps { get; set; }

        public AnchorNamePreset AnchorPreset { get; set; }

        public AnchorNamePreset ExplicitAnchorPreset { get; set; }

        public Rect LayoutRect { get; set; }

        public bool HasLayoutRect { get; set; }
    }
}
