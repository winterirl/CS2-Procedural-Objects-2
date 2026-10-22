using UnityEngine;

namespace PO2.Materials
{
    // Settings for generated UVs. Original mapping keeps the source UV payload.
    public sealed class TextureMappingData
    {
        private const float kMinimumTextureScale = 0.001f;

        public TextureMappingMode Mode { get; private set; }
        public Vector2 TextureScale { get; private set; }
        public float Rotation { get; private set; }
        public Vector2 Offset { get; private set; }

        public TextureMappingData(TextureMappingMode mode = TextureMappingMode.Original, Vector2 textureScale = default, float rotation = 0f, Vector2 offset = default)
        {
            Mode = mode;
            // default(Vector2) is treated as "unspecified" rather than a request
            // for a zero scale, which would collapse every generated UV.
            TextureScale = textureScale == default ? Vector2.one : ClampTextureScale(textureScale);
            Rotation = rotation;
            Offset = offset;
        }

        public bool SetMode(TextureMappingMode mode)
        {
            if (Mode == mode)
            {
                return false;
            }

            Mode = mode;
            return true;
        }

        public bool SetTextureScale(Vector2 textureScale)
        {
            textureScale = ClampTextureScale(textureScale);

            if (Mathf.Approximately(TextureScale.x, textureScale.x) && Mathf.Approximately(TextureScale.y, textureScale.y))
            {
                return false;
            }

            TextureScale = textureScale;
            return true;
        }

        public bool SetRotation(float rotation)
        {
            if (Mathf.Approximately(Rotation, rotation))
            {
                return false;
            }

            Rotation = rotation;
            return true;
        }

        public bool SetOffset(Vector2 offset)
        {
            if (Mathf.Approximately(Offset.x, offset.x) && Mathf.Approximately(Offset.y, offset.y))
            {
                return false;
            }

            Offset = offset;
            return true;
        }

        public TextureMappingData Clone()
        {
            return new TextureMappingData(Mode, TextureScale, Rotation, Offset);
        }

        private static Vector2 ClampTextureScale(Vector2 value)
        {
            // Negative/zero scales are not supported by the current mapping UI or generator.
            // The small floor also avoids division by zero later.
            return new Vector2(Mathf.Max(kMinimumTextureScale, value.x), Mathf.Max(kMinimumTextureScale, value.y));
        }
    }
}