using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace MapLibre.Unity.Plateau
{
    /// <summary>
    /// Parses the 3D Tiles "b3dm" (Batched 3D Model) tile format: a small fixed header, a feature table, a batch
    /// table (all ignored here except for locating the embedded glTF), followed by an embedded glb. PLATEAU's b3dm
    /// tiles embed a glTF using the CESIUM_RTC extension (RTC = "relative to center": vertex positions are stored
    /// relative to an ECEF center point, listed in extensions.CESIUM_RTC.center, to preserve float precision at
    /// planetary scale) combined with KHR_draco_mesh_compression. glTFast has no CESIUM_RTC support, and since
    /// PLATEAU lists it under extensionsRequired, glTFast would refuse to load the glb outright. This type strips
    /// the CESIUM_RTC extension references from the glb's JSON chunk (after extracting its RTC center) and repacks
    /// a sanitized glb that glTFast can load; the RTC center is then applied manually as the tile's world position.
    /// </summary>
    public static class B3dm
    {
        private const uint B3dmMagic = 0x6D643362; // "b3dm" little-endian ASCII, i.e. bytes 'b','3','d','m'.
        private const uint GlbMagic = 0x46546C67; // "glTF" little-endian ASCII.
        private const uint GlbJsonChunkType = 0x4E4F534A; // "JSON" little-endian ASCII.
        private const int B3dmHeaderLength = 28;

        /// <summary>
        /// Attempts to parse a b3dm buffer, returning a glTF-Binary (.glb) buffer with the CESIUM_RTC extension
        /// stripped out (so glTFast can load it) plus the RTC center (ECEF meters) that was removed, if present.
        /// </summary>
        /// <param name="b3dm">Raw bytes of a .b3dm file.</param>
        /// <param name="sanitizedGlb">On success, a self-contained glb buffer with CESIUM_RTC removed.</param>
        /// <param name="rtcCenterEcef">On success, the ECEF center in meters ({X,Y,Z}) from CESIUM_RTC, or null if the source glTF didn't use CESIUM_RTC.</param>
        /// <param name="error">On failure, a human-readable description of what went wrong.</param>
        public static bool TryParse(byte[] b3dm, out byte[] sanitizedGlb, out double[] rtcCenterEcef, out string error)
        {
            sanitizedGlb = null;
            rtcCenterEcef = null;
            error = null;

            try
            {
                if (b3dm == null || b3dm.Length < B3dmHeaderLength)
                {
                    error = "buffer is null or shorter than the b3dm header.";
                    return false;
                }

                uint magic = BitConverter.ToUInt32(b3dm, 0);
                if (magic != B3dmMagic)
                {
                    error = $"unexpected magic 0x{magic:X8} (expected b3dm).";
                    return false;
                }

                uint featureTableJsonLength = BitConverter.ToUInt32(b3dm, 12);
                uint featureTableBinLength = BitConverter.ToUInt32(b3dm, 16);
                uint batchTableJsonLength = BitConverter.ToUInt32(b3dm, 20);
                uint batchTableBinLength = BitConverter.ToUInt32(b3dm, 24);

                long glbOffset = B3dmHeaderLength + (long)featureTableJsonLength + featureTableBinLength + batchTableJsonLength + batchTableBinLength;
                if (glbOffset < B3dmHeaderLength || glbOffset >= b3dm.Length)
                {
                    error = $"computed glb offset {glbOffset} is out of range for a buffer of length {b3dm.Length}.";
                    return false;
                }

                return TryParseAndSanitizeGlb(b3dm, (int)glbOffset, out sanitizedGlb, out rtcCenterEcef, out error);
            }
            catch (Exception e)
            {
                error = $"unhandled exception while parsing b3dm: {e.Message}";
                return false;
            }
        }

        private static bool TryParseAndSanitizeGlb(byte[] buffer, int glbOffset, out byte[] sanitizedGlb, out double[] rtcCenterEcef, out string error)
        {
            sanitizedGlb = null;
            rtcCenterEcef = null;
            error = null;

            int remaining = buffer.Length - glbOffset;
            if (remaining < 12)
            {
                error = "not enough bytes remaining for a glb header.";
                return false;
            }

            uint glbMagic = BitConverter.ToUInt32(buffer, glbOffset);
            if (glbMagic != GlbMagic)
            {
                error = $"unexpected glb magic 0x{glbMagic:X8}.";
                return false;
            }

            uint glbVersion = BitConverter.ToUInt32(buffer, glbOffset + 4);
            if (glbVersion != 2)
            {
                error = $"unsupported glb version {glbVersion} (expected 2).";
                return false;
            }

            // First chunk must be JSON per the glb spec.
            int cursor = glbOffset + 12;
            uint jsonChunkLength = BitConverter.ToUInt32(buffer, cursor);
            uint jsonChunkType = BitConverter.ToUInt32(buffer, cursor + 4);
            if (jsonChunkType != GlbJsonChunkType)
            {
                error = "first glb chunk is not JSON.";
                return false;
            }

            int jsonStart = cursor + 8;
            string json = Encoding.UTF8.GetString(buffer, jsonStart, (int)jsonChunkLength);

            JObject root = JObject.Parse(json);

            rtcCenterEcef = ExtractAndStripCesiumRtc(root);

            byte[] sanitizedJsonBytes = Encoding.UTF8.GetBytes(root.ToString(Newtonsoft.Json.Formatting.None));

            // The remaining bytes after the JSON chunk (typically just one BIN chunk, but copied verbatim as a
            // block so this also tolerates additional/unknown chunk types without needing to parse them).
            int afterJsonChunk = jsonStart + (int)jsonChunkLength;
            int trailingLength = buffer.Length - afterJsonChunk;

            sanitizedGlb = BuildGlb(sanitizedJsonBytes, buffer, afterJsonChunk, trailingLength);
            return true;
        }

        /// <summary>
        /// If the glTF JSON declares the CESIUM_RTC extension, extracts its ECEF center and removes all references
        /// to CESIUM_RTC from extensionsUsed/extensionsRequired/extensions so glTFast (which has no CESIUM_RTC
        /// support) can load the result. Returns null if CESIUM_RTC was not present (not an error - the vertex
        /// data is then already in the glTF's own local space with no RTC offset).
        /// </summary>
        private static double[] ExtractAndStripCesiumRtc(JObject root)
        {
            double[] center = null;

            if (root["extensions"] is JObject extensions && extensions["CESIUM_RTC"] is JObject cesiumRtc)
            {
                if (cesiumRtc["center"] is JArray centerArray && centerArray.Count == 3)
                {
                    center = new[]
                    {
                        centerArray[0].Value<double>(),
                        centerArray[1].Value<double>(),
                        centerArray[2].Value<double>(),
                    };
                }

                extensions.Remove("CESIUM_RTC");
                if (!extensions.HasValues)
                {
                    root.Remove("extensions");
                }
            }

            RemoveFromStringArray(root, "extensionsUsed", "CESIUM_RTC");
            RemoveFromStringArray(root, "extensionsRequired", "CESIUM_RTC");

            return center;
        }

        private static void RemoveFromStringArray(JObject root, string propertyName, string value)
        {
            if (!(root[propertyName] is JArray array))
            {
                return;
            }

            for (int i = array.Count - 1; i >= 0; i--)
            {
                if (string.Equals((string)array[i], value, StringComparison.Ordinal))
                {
                    array.RemoveAt(i);
                }
            }

            if (array.Count == 0)
            {
                root.Remove(propertyName);
            }
        }

        /// <summary>
        /// Reassembles a glb buffer from a (possibly resized) JSON chunk plus the remaining chunks copied verbatim.
        /// Per the glb spec, the JSON chunk must be padded with trailing spaces (0x20) to a 4-byte boundary, and
        /// the 12-byte glb header's total length field must reflect the new overall size.
        /// </summary>
        private static byte[] BuildGlb(byte[] jsonBytes, byte[] sourceBuffer, int trailingStart, int trailingLength)
        {
            int paddedJsonLength = (jsonBytes.Length + 3) & ~3;
            int totalLength = 12 + 8 + paddedJsonLength + trailingLength;

            var output = new byte[totalLength];
            using (var writer = new BinaryWriter(new MemoryStream(output)))
            {
                writer.Write(GlbMagic);
                writer.Write((uint)2);
                writer.Write((uint)totalLength);

                writer.Write((uint)paddedJsonLength);
                writer.Write(GlbJsonChunkType);
                writer.Write(jsonBytes);
                for (int i = jsonBytes.Length; i < paddedJsonLength; i++)
                {
                    writer.Write((byte)0x20); // Space padding, as required by the glb spec for the JSON chunk.
                }

                writer.Write(sourceBuffer, trailingStart, trailingLength);
            }

            return output;
        }
    }
}
