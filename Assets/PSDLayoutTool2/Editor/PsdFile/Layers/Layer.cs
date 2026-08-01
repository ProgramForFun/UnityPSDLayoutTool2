namespace PhotoshopFile
{
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Globalization;
    using System.IO;
    using System.Text;
    using UnityEngine;

    /// <summary>
    /// PSD 图层分段类型。
    /// </summary>
    public enum LayerSectionType
    {
        /// <summary>
        /// 普通图层或未声明分段信息。
        /// </summary>
        Other = 0,

        /// <summary>
        /// 展开的图层组起点。
        /// </summary>
        OpenFolder = 1,

        /// <summary>
        /// 折叠的图层组起点。
        /// </summary>
        ClosedFolder = 2,

        /// <summary>
        /// 图层组结束标记。
        /// </summary>
        BoundingSectionDivider = 3
    }

    /// <summary>
    /// Contains the data representation of a PSD layer
    /// </summary>
    public class Layer
    {
        /// <summary>
        /// The bit flag representing transparency being protected.
        /// </summary>
        private static readonly int ProtectTransparencyBit = BitVector32.CreateMask();

        /// <summary>
        /// The bit flag representing the layer being visible.
        /// </summary>
        private static readonly int VisibleBit = BitVector32.CreateMask(ProtectTransparencyBit);

        /// <summary>
        /// The bit flag representing the layer being obsolete.  ???
        /// </summary>
        private static readonly int ObsoleteBit = BitVector32.CreateMask(VisibleBit);

        /// <summary>
        /// The bit flag representing the layer being version 5+.  ???
        /// </summary>
        private static readonly int Version5OrLaterBit = BitVector32.CreateMask(ObsoleteBit);

        /// <summary>
        /// The bit flag representing the layer's pixel data being irrelevant (a group layer, for example).
        /// </summary>
        private static readonly int PixelDataIrrelevantBit = BitVector32.CreateMask(Version5OrLaterBit);

        /// <summary>
        /// The set of flags associated with this layer.
        /// </summary>
        private BitVector32 flags;

        /// <summary>
        /// Initializes a new instance of the <see cref="Layer"/> class using the provided reader containing the PSD file data.
        /// </summary>
        /// <param name="reader">The reader containing the PSD file data.</param>
        /// <param name="psdFile">The PSD file to set as the parent.</param>
        public Layer(BinaryReverseReader reader, PsdFile psdFile)
        {
            Children = new List<Layer>();
            PsdFile = psdFile;

            // read the rect
            Rect rect = new Rect();
            rect.y = reader.ReadInt32();
            rect.x = reader.ReadInt32();
            rect.height = reader.ReadInt32() - rect.y;
            rect.width = reader.ReadInt32() - rect.x;
            Rect = rect;

            // read the channels
            int channelCount = reader.ReadUInt16();
            Channels = new List<Channel>();
            SortedChannels = new SortedList<short, Channel>();
            for (int index = 0; index < channelCount; ++index)
            {
                Channel channel = new Channel(reader, this);
                Channels.Add(channel);
                SortedChannels.Add(channel.ID, channel);
            }

            // read the header and verify it
            if (new string(reader.ReadChars(4)) != "8BIM")
            {
                throw new IOException("Layer Channelheader error!");
            }

            // read the blend mode key (unused) (defaults to "norm")
            reader.ReadChars(4);

            // read the opacity
            Opacity = reader.ReadByte();

            // read the clipping (unused) (< 0 = base, > 0 = non base)
            reader.ReadByte();

            // read all of the flags (protectTrans, visible, obsolete, ver5orLater, pixelDataIrrelevant)
            flags = new BitVector32(reader.ReadByte());

            // skip a padding byte
            reader.ReadByte();

            uint num3 = reader.ReadUInt32();
            long position1 = reader.BaseStream.Position;
            MaskData = new Mask(reader, this);
            BlendingRangesData = new BlendingRanges(reader);
            long position2 = reader.BaseStream.Position;

            // read the name
            Name = reader.ReadPascalString();

            // read the adjustment info
            int count = (int)((reader.BaseStream.Position - position2) % 4L);
            reader.ReadBytes(count);
            AdjustmentInfo = new List<AdjustmentLayerInfo>();
            long num4 = position1 + num3;
            while (reader.BaseStream.Position < num4)
            {
                try
                {
                    AdjustmentInfo.Add(new AdjustmentLayerInfo(reader, this));
                }
                catch
                {
                    reader.BaseStream.Position = num4;
                }
            }

            foreach (AdjustmentLayerInfo adjustmentLayerInfo in AdjustmentInfo)
            {
                if (adjustmentLayerInfo.Key == "TySh")
                {
                    ReadTextLayer(adjustmentLayerInfo.DataReader);
                }
                else if (adjustmentLayerInfo.Key == "luni")
                {
                    // read the unicode name
                    BinaryReverseReader dataReader = adjustmentLayerInfo.DataReader;
                    dataReader.ReadBytes(3);
                    dataReader.ReadByte();
                    Name = dataReader.ReadString().TrimEnd(new char[1]);
                }
                else if (adjustmentLayerInfo.Key == "lsct" || adjustmentLayerInfo.Key == "lsdk")
                {
                    ReadLayerSectionType(adjustmentLayerInfo.DataReader);
                }
            }

            reader.BaseStream.Position = num4;
        }

        #region Properties

        #region Text Layer Properties

        /// <summary>
        /// Gets a value indicating whether this layer is a text layer.
        /// </summary>
        public bool IsTextLayer { get; private set; }

        /// <summary>
        /// Gets the actual text string, if this is a text layer.
        /// </summary>
        public string Text { get; private set; }

        /// <summary>
        /// Gets the point size of the font, if this is a text layer.
        /// </summary>
        public float FontSize { get; private set; }

        /// <summary>
        /// Gets the name of the font used, if this is a text layer.
        /// </summary>
        public string FontName { get; private set; }

        /// <summary>
        /// Gets the justification of the text, if this is a text layer.
        /// </summary>
        public TextJustification Justification { get; private set; }

        /// <summary>
        /// Gets the Fill Color of the text, if this is a text layer.
        /// </summary>
        public Color FillColor { get; private set; }

        /// <summary>
        /// Gets the style of warp done on the text, if it is a text layer.
        /// Can be warpNone, warpTwist, etc.
        /// </summary>
        public string WarpStyle { get; private set; }

        #endregion

        /// <summary>
        /// Gets a list of the children <see cref="Layer"/>s that belong to this Layer.
        /// </summary>
        public List<Layer> Children { get; private set; }

        /// <summary>
        /// Gets or sets a value indicating whether this layer has Effects/Styles or not.
        /// </summary>
        public bool HasEffects { get; set; }

        /// <summary>
        /// Gets the rectangle containing the contents of the layer.
        /// </summary>
        public Rect Rect { get; private set; }

        /// <summary>
        /// Gets a list of the Channel information.
        /// </summary>
        public List<Channel> Channels { get; private set; }

        /// <summary>
        /// Gets a sorted list of Channel information.
        /// </summary>
        public SortedList<short, Channel> SortedChannels { get; private set; }

        /// <summary>
        /// Gets the opacity of this layer.  0 = transparent and 255 = opaque/solid.
        /// </summary>
        public byte Opacity { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this layer is visible or not.
        /// </summary>
        public bool Visible
        {
            get
            {
                return !flags[VisibleBit];
            }
        }

        /// <summary>
        /// Gets a value indicating whether this layer's pixel data is irrelevant.  This is often the case with group layers.
        /// </summary>
        public bool IsPixelDataIrrelevant
        {
            get
            {
                return flags[PixelDataIrrelevantBit];
            }
        }

        /// <summary>
        /// 获取此图层是否包含 PSD 标准分段信息。
        /// </summary>
        public bool HasSectionDividerInfo { get; private set; }

        /// <summary>
        /// 获取 PSD 标准分段类型。
        /// </summary>
        public LayerSectionType SectionType { get; private set; }

        /// <summary>
        /// Gets or sets the name of the layer.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets the mask data for this layer.
        /// </summary>
        public Mask MaskData { get; private set; }

        /// <summary>
        /// Gets the <see cref="PsdFile"/> that this <see cref="Layer"/> belongs to.
        /// </summary>
        internal PsdFile PsdFile { get; private set; }

        /// <summary>
        /// Gets or sets the blending ranges data for this layer.
        /// </summary>
        private BlendingRanges BlendingRangesData { get; set; }

        /// <summary>
        /// Gets or sets the list of adjustment information for this layer.
        /// </summary>
        private List<AdjustmentLayerInfo> AdjustmentInfo { get; set; }

        #endregion

        /// <summary>
        /// Reads the text information for the layer.
        /// </summary>
        /// <param name="dataReader">The reader to use to read the text data.</param>
        private void ReadTextLayer(BinaryReverseReader dataReader)
        {
            IsTextLayer = true;
            Text = Name ?? string.Empty;
            Justification = TextJustification.Left;
            FontSize = Rect.height > 0 ? Rect.height : 16f;
            FillColor = Color.white;
            FontName = string.Empty;
            WarpStyle = string.Empty;

            if (dataReader == null || dataReader.BaseStream.Length <= 0 || dataReader.BaseStream.Length > int.MaxValue)
            {
                return;
            }

            dataReader.BaseStream.Position = 0;
            byte[] data = dataReader.ReadBytes((int)dataReader.BaseStream.Length);

            string text;
            int ignoredEndIndex;
            if (TryReadDescriptorUnicodeString(data, "Txt TEXT", out text) ||
                TryReadEngineDataString(data, "/Text (", 0, true, out text, out ignoredEndIndex))
            {
                Text = text;
            }

            int justification;
            if (TryReadAsciiInt(data, "/Justification ", 0, out justification))
            {
                if (justification == 1)
                {
                    Justification = TextJustification.Right;
                }
                else if (justification == 2)
                {
                    Justification = TextJustification.Center;
                }
            }

            float fontSize;
            if (TryReadAsciiFloat(data, "/FontSize ", 0, out fontSize) && fontSize > 0f)
            {
                FontSize = fontSize;
            }

            Color fillColor;
            if (TryReadFillColor(data, out fillColor))
            {
                FillColor = fillColor;
            }

            string fontName;
            if (TryReadFontName(data, out fontName))
            {
                FontName = fontName;
            }

            string warpStyle;
            if (TryReadWarpStyle(data, out warpStyle))
            {
                WarpStyle = warpStyle;
            }
        }

        /// <summary>
        /// Reads the standard Unicode text value stored in the TySh action descriptor.
        /// </summary>
        private static bool TryReadDescriptorUnicodeString(byte[] data, string marker, out string value)
        {
            value = string.Empty;
            int markerIndex = FindAsciiToken(data, marker, 0);
            if (markerIndex < 0)
            {
                return false;
            }

            int lengthIndex = markerIndex + marker.Length;
            int characterCount;
            if (!TryReadInt32BigEndian(data, lengthIndex, out characterCount) || characterCount < 0)
            {
                return false;
            }

            int textIndex = lengthIndex + sizeof(int);
            long byteCount = (long)characterCount * 2L;
            if (byteCount > int.MaxValue || textIndex + byteCount > data.Length)
            {
                return false;
            }

            value = Encoding.BigEndianUnicode.GetString(data, textIndex, (int)byteCount).TrimEnd('\0');
            return true;
        }

        /// <summary>
        /// Reads a Photoshop EngineData literal string such as /Text (...).
        /// </summary>
        private static bool TryReadEngineDataString(
            byte[] data,
            string marker,
            int startIndex,
            bool trimTrailingCarriageReturn,
            out string value,
            out int endIndex)
        {
            value = string.Empty;
            endIndex = startIndex;

            int markerIndex = FindAsciiToken(data, marker, startIndex);
            if (markerIndex < 0)
            {
                return false;
            }

            int valueIndex = markerIndex + marker.Length;
            if (valueIndex + 1 < data.Length && data[valueIndex] == 0xFE && data[valueIndex + 1] == 0xFF)
            {
                return TryReadUtf16EngineDataString(data, valueIndex + 2, true, trimTrailingCarriageReturn, out value, out endIndex);
            }

            if (valueIndex + 1 < data.Length && data[valueIndex] == 0xFF && data[valueIndex + 1] == 0xFE)
            {
                return TryReadUtf16EngineDataString(data, valueIndex + 2, false, trimTrailingCarriageReturn, out value, out endIndex);
            }

            StringBuilder builder = new StringBuilder();
            bool escaped = false;
            for (int index = valueIndex; index < data.Length; ++index)
            {
                byte current = data[index];
                if (!escaped && current == (byte)')')
                {
                    value = builder.ToString();
                    endIndex = index + 1;
                    return true;
                }

                if (!escaped && current == (byte)'\\')
                {
                    escaped = true;
                    continue;
                }

                builder.Append((char)current);
                escaped = false;
            }

            return false;
        }

        /// <summary>
        /// Reads a BOM-prefixed UTF-16 EngineData string up to its ASCII closing parenthesis.
        /// </summary>
        private static bool TryReadUtf16EngineDataString(
            byte[] data,
            int valueIndex,
            bool bigEndian,
            bool trimTrailingCarriageReturn,
            out string value,
            out int endIndex)
        {
            StringBuilder builder = new StringBuilder();
            for (int index = valueIndex; index < data.Length; index += 2)
            {
                if (data[index] == (byte)')')
                {
                    value = builder.ToString();
                    if (trimTrailingCarriageReturn && value.EndsWith("\r"))
                    {
                        value = value.Substring(0, value.Length - 1);
                    }

                    endIndex = index + 1;
                    return true;
                }

                if (index + 1 >= data.Length)
                {
                    break;
                }

                ushort codeUnit = bigEndian
                    ? (ushort)((data[index] << 8) | data[index + 1])
                    : (ushort)(data[index] | (data[index + 1] << 8));
                builder.Append((char)codeUnit);
            }

            value = string.Empty;
            endIndex = valueIndex;
            return false;
        }

        /// <summary>
        /// Reads the first fill color array following /FillColor.
        /// </summary>
        private static bool TryReadFillColor(byte[] data, out Color color)
        {
            color = Color.white;
            int fillColorIndex = FindAsciiToken(data, "/FillColor", 0);
            int valuesIndex = FindAsciiToken(data, "/Values [", fillColorIndex >= 0 ? fillColorIndex : 0);
            if (fillColorIndex < 0 || valuesIndex < 0)
            {
                return false;
            }

            int valueIndex = valuesIndex + "/Values [".Length;
            float alpha;
            float red;
            float green;
            float blue;
            if (!TryReadNextAsciiFloat(data, ref valueIndex, out alpha) ||
                !TryReadNextAsciiFloat(data, ref valueIndex, out red) ||
                !TryReadNextAsciiFloat(data, ref valueIndex, out green) ||
                !TryReadNextAsciiFloat(data, ref valueIndex, out blue))
            {
                return false;
            }

            color = new Color(red, green, blue, alpha);
            return true;
        }

        /// <summary>
        /// Resolves the font selected by the first style run against the EngineData font set.
        /// </summary>
        private static bool TryReadFontName(byte[] data, out string fontName)
        {
            fontName = string.Empty;
            int fontSetIndex = FindAsciiToken(data, "/FontSet [", 0);
            if (fontSetIndex < 0)
            {
                return false;
            }

            int selectedFontIndex = 0;
            TryReadAsciiInt(data, "/Font ", 0, out selectedFontIndex);
            selectedFontIndex = selectedFontIndex < 0 ? 0 : selectedFontIndex;

            int searchIndex = fontSetIndex + "/FontSet [".Length;
            string firstFontName = string.Empty;
            for (int index = 0; index <= selectedFontIndex; ++index)
            {
                string currentFontName;
                int endIndex;
                if (!TryReadEngineDataString(data, "/Name (", searchIndex, false, out currentFontName, out endIndex))
                {
                    break;
                }

                if (index == 0)
                {
                    firstFontName = currentFontName;
                }

                if (index == selectedFontIndex)
                {
                    fontName = currentFontName;
                    return !string.IsNullOrEmpty(fontName);
                }

                searchIndex = endIndex;
            }

            fontName = firstFontName;
            return !string.IsNullOrEmpty(fontName);
        }

        /// <summary>
        /// Reads the warp enum value from the binary TySh descriptor when present.
        /// </summary>
        private static bool TryReadWarpStyle(byte[] data, out string warpStyle)
        {
            warpStyle = string.Empty;
            int firstIndex = FindAsciiToken(data, "warpStyle", 0);
            int secondIndex = FindAsciiToken(data, "warpStyle", firstIndex >= 0 ? firstIndex + "warpStyle".Length : 0);
            if (firstIndex < 0 || secondIndex < 0)
            {
                return false;
            }

            int lengthIndex = secondIndex + "warpStyle".Length;
            int length;
            if (!TryReadInt32BigEndian(data, lengthIndex, out length) || length <= 0)
            {
                return false;
            }

            int valueIndex = lengthIndex + sizeof(int);
            if (valueIndex + length > data.Length)
            {
                return false;
            }

            warpStyle = Encoding.ASCII.GetString(data, valueIndex, length);
            return true;
        }

        /// <summary>
        /// Reads an ASCII integer following the given marker.
        /// </summary>
        private static bool TryReadAsciiInt(byte[] data, string marker, int startIndex, out int value)
        {
            value = 0;
            float floatValue;
            if (!TryReadAsciiFloat(data, marker, startIndex, out floatValue))
            {
                return false;
            }

            value = (int)floatValue;
            return true;
        }

        /// <summary>
        /// Reads an ASCII float following the given marker.
        /// </summary>
        private static bool TryReadAsciiFloat(byte[] data, string marker, int startIndex, out float value)
        {
            value = 0f;
            int markerIndex = FindAsciiToken(data, marker, startIndex);
            if (markerIndex < 0)
            {
                return false;
            }

            int valueIndex = markerIndex + marker.Length;
            return TryReadNextAsciiFloat(data, ref valueIndex, out value);
        }

        /// <summary>
        /// Reads the next whitespace-delimited ASCII float from a byte array.
        /// </summary>
        private static bool TryReadNextAsciiFloat(byte[] data, ref int index, out float value)
        {
            value = 0f;
            while (index < data.Length && IsAsciiWhitespace(data[index]))
            {
                ++index;
            }

            int startIndex = index;
            while (index < data.Length && IsAsciiNumberCharacter(data[index]))
            {
                ++index;
            }

            if (startIndex == index)
            {
                return false;
            }

            string number = Encoding.ASCII.GetString(data, startIndex, index - startIndex);
            return float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// Finds an ASCII marker without moving a stream or reading beyond its bounds.
        /// </summary>
        private static int FindAsciiToken(byte[] data, string marker, int startIndex)
        {
            if (data == null || string.IsNullOrEmpty(marker) || startIndex < 0)
            {
                return -1;
            }

            int lastStartIndex = data.Length - marker.Length;
            for (int index = startIndex; index <= lastStartIndex; ++index)
            {
                bool matches = true;
                for (int markerIndex = 0; markerIndex < marker.Length; ++markerIndex)
                {
                    if (data[index + markerIndex] != (byte)marker[markerIndex])
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>
        /// Reads a signed big-endian Int32 without throwing on truncated data.
        /// </summary>
        private static bool TryReadInt32BigEndian(byte[] data, int index, out int value)
        {
            value = 0;
            if (data == null || index < 0 || index > data.Length - sizeof(int))
            {
                return false;
            }

            value = (data[index] << 24) |
                (data[index + 1] << 16) |
                (data[index + 2] << 8) |
                data[index + 3];
            return true;
        }

        /// <summary>
        /// Returns whether a byte is whitespace in EngineData.
        /// </summary>
        private static bool IsAsciiWhitespace(byte value)
        {
            return value == (byte)' ' || value == (byte)'\t' || value == (byte)'\r' || value == (byte)'\n';
        }

        /// <summary>
        /// Returns whether a byte can occur in an EngineData number.
        /// </summary>
        private static bool IsAsciiNumberCharacter(byte value)
        {
            return (value >= (byte)'0' && value <= (byte)'9') ||
                value == (byte)'+' ||
                value == (byte)'-' ||
                value == (byte)'.' ||
                value == (byte)'e' ||
                value == (byte)'E';
        }

        /// <summary>
        /// 读取 PSD 标准图层分段类型。
        /// </summary>
        /// <param name="dataReader">lsct 或 lsdk 附加图层信息的读取器。</param>
        private void ReadLayerSectionType(BinaryReverseReader dataReader)
        {
            if (dataReader == null || dataReader.BaseStream.Length < sizeof(int))
            {
                return;
            }

            SectionType = (LayerSectionType)dataReader.ReadInt32();
            HasSectionDividerInfo = true;
        }
    }
}
