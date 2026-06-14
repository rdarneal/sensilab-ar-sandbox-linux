//
//  UITextureFactory.cs
//
//  Procedurally builds every sprite/texture the glassmorphism theme needs, so the
//  refresh ships no binary image assets and nothing has to be wired in the editor:
//    - a rounded-rect FILL sprite (9-sliced) for panel/button glass
//    - a rounded-rect BORDER ring sprite (9-sliced) for the glowing edge
//    - a tileable GRID texture for the ambient background
//    - a radial GLOW texture for the per-simulation backdrop bloom
//  All four are white so callers tint them via Image/RawImage colour. Results are
//  cached — the textures are generated once per run.
//

using UnityEngine;

namespace ARSandbox.UI
{
    public static class UITextureFactory
    {
        private static Sprite _fill;
        private static Sprite _border;
        private static Texture2D _grid;
        private static Texture2D _glow;

        // White rounded-rect, soft 1px antialiased edge. Tint via Image.color.
        public static Sprite RoundedFillSprite => _fill != null ? _fill : (_fill = BuildRounded(false));

        // White rounded-rect outline ring of SandboxUITheme.BorderThickness. Tint to the
        // accent for an active/hovered glow, or BorderRest when idle.
        public static Sprite RoundedBorderSprite => _border != null ? _border : (_border = BuildRounded(true));

        public static Texture2D GridTexture => _grid != null ? _grid : (_grid = BuildGrid());

        public static Texture2D GlowTexture => _glow != null ? _glow : (_glow = BuildGlow());

        // --- rounded-rect builder -------------------------------------------------
        // Builds an NxN texture where N = 2*R + C: corner slices are R px (the rounded
        // corners), the centre slice is C px (stretched by 9-slicing). `ring` draws only
        // the edge stroke; otherwise the interior is filled.
        private static Sprite BuildRounded(bool ring)
        {
            int r = SandboxUITheme.CornerRadius;
            int t = SandboxUITheme.BorderThickness;
            int center = 4;
            int n = 2 * r + center;

            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            float b = n / 2f; // half size
            var pixels = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    // Signed distance to a rounded rect centred in the texture (d<=0 inside).
                    float qx = Mathf.Abs(x + 0.5f - b) - (b - r);
                    float qy = Mathf.Abs(y + 0.5f - b) - (b - r);
                    float ax = Mathf.Max(qx, 0f);
                    float ay = Mathf.Max(qy, 0f);
                    float d = Mathf.Sqrt(ax * ax + ay * ay) + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;

                    float a;
                    if (ring)
                    {
                        // Ring = inside the outer edge (d<=0) AND outside the inner edge (d>=-t).
                        float outer = Mathf.Clamp01(0.5f - d);
                        float inner = Mathf.Clamp01(0.5f + d + t);
                        a = outer * inner;
                    }
                    else
                    {
                        a = Mathf.Clamp01(0.5f - d); // filled interior with AA edge
                    }

                    pixels[y * n + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();

            var border = new Vector4(r, r, r, r);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f),
                                 100f, 0, SpriteMeshType.FullRect, border);
        }

        // --- ambient grid ---------------------------------------------------------
        // One tile: faint lines on the top + left edges so the texture tiles seamlessly
        // into a continuous grid. White; tint + tile via the consuming RawImage.
        private static Texture2D BuildGrid()
        {
            const int cell = 48;
            const float lineAlpha = 0.05f;
            var tex = new Texture2D(cell, cell, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat,
            };

            var pixels = new Color[cell * cell];
            for (int y = 0; y < cell; y++)
            {
                for (int x = 0; x < cell; x++)
                {
                    bool line = (x == 0) || (y == 0);
                    pixels[y * cell + x] = new Color(1f, 1f, 1f, line ? lineAlpha : 0f);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        // --- radial glow ----------------------------------------------------------
        // Smooth white-centre → transparent-edge disc. Tinted to the active accent and
        // scaled large to bloom the preview/background.
        private static Texture2D BuildGlow()
        {
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            float c = size / 2f;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(c, c)) / c;
                    // smoothstep falloff, squared for a softer core-to-edge bloom
                    float a = Mathf.Clamp01(1f - dist);
                    a = a * a * (3f - 2f * a);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
