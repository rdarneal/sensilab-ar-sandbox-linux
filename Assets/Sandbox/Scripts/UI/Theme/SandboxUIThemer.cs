//
//  SandboxUIThemer.cs
//
//  Applies the dark-glassmorphism theme to the existing UI at runtime, so the refresh
//  needs no scene-YAML surgery and is fully reversible (delete the Theme/ scripts and
//  the original look returns). It self-bootstraps after scene load, walks the canvas
//  once everything has been instantiated, and:
//    - swaps panel/button backgrounds to the rounded glass sprite + adds a glow border
//    - retypes text to Outfit (body) / JetBrains Mono (small uppercase labels)
//    - tints the per-simulation accent across borders + the optional background glow
//
//  The full-screen ambient grid + radial glow are OFF by default: in a live sandbox the
//  canvas may sit over the projected sand, where an opaque backdrop would hide it. Flip
//  EnableBackground once that's confirmed safe.
//

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ARSandbox.UI
{
    public class SandboxUIThemer : MonoBehaviour
    {
        // Full-screen ambient grid + central radial glow. The operator UI lives on its
        // own area (not over the live projection), so the mockup backdrop is safe to draw.
        public bool EnableBackground = true;

        // Decorative art lives in custom SPRITES (logos, dials, icons), not in the
        // GameObject name — matching the GO name wrongly skipped panels like "SensilabLogo"
        // (a plain card behind the logo) and left them un-themed. So art is detected by the
        // sprite's name instead; the built-in Unity UI sprite ("UISprite"/"Background") is
        // a themable background, a custom sprite is art to preserve.
        private static readonly string[] ArtSpriteFragments =
        {
            "logo", "monash", "sensilab", "metaball", "dial", "circle", "rubbish", "bin",
            "icon", "arrow", "cursor", "scale", "transform",
        };

        // Structural Images we must never paint over (camera/mask viewports, scrollbars):
        // glassing these would hide the depth preview or break masking. Matched on GO name.
        private static readonly string[] StructuralNameFragments =
        {
            "viewport", "mask", "scrollbar", "rawimage",
        };

        // Min RectTransform edge (px) for a non-interactive Image to count as a panel.
        private const float PanelMinSize = 48f;
        // A panel only gets a glowing border if it was a solid card to begin with; faint
        // container backers (low original alpha) stay borderless so they don't read as
        // phantom pills.
        private const float SolidPanelAlpha = 0.5f;

        private readonly List<Graphic> borderGraphics = new List<Graphic>();
        private RawImage glowImage;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            // Attach to the first Canvas found. One themer handles the whole canvas tree.
            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null) return;
            if (canvas.GetComponentInParent<SandboxUIThemer>() != null) return;

            var go = canvas.gameObject;
            if (go.GetComponent<SandboxUIThemer>() == null)
            {
                go.AddComponent<SandboxUIThemer>();
            }
        }

        private void OnEnable()
        {
            ModeSelector.OnModeChanged += OnModeChanged;
        }

        private void OnDisable()
        {
            ModeSelector.OnModeChanged -= OnModeChanged;
        }

        private void Start()
        {
            // Wait a frame so manager Start()/Awake() have finished building dynamic UI
            // and the layout has run (RectTransform sizes are valid).
            StartCoroutine(ApplyNextFrame());
        }

        private IEnumerator ApplyNextFrame()
        {
            yield return null;

            if (SandboxUITheme.Sans == null)
            {
                Debug.LogWarning("[SandboxUIThemer] Outfit font missing from Resources/Fonts — " +
                                 "text retype skipped. Glass styling still applies.");
            }

            if (EnableBackground) BuildBackground();
            ThemeCanvas();
            OnModeChanged(SandboxSimType.None); // prime the default (coral) accent
        }

        // --- canvas walk ----------------------------------------------------------
        private void ThemeCanvas()
        {
            int imgCount = 0, textCount = 0;

            // include inactive: most simulation menus start disabled.
            var images = GetComponentsInChildren<Image>(true);
            foreach (var img in images)
            {
                if (IsOwnedByTheme(img.transform)) continue;
                if (!IsThemableBackground(img)) continue;

                // Per-element guard: one bad element must never abort the whole pass
                // (that would leave later text un-recoloured and invisible on dark panels).
                try
                {
                    var selectable = img.GetComponent<Selectable>();
                    if (selectable != null)
                    {
                        StyleButton(img, selectable);
                    }
                    else
                    {
                        // Recolor every themable background regardless of size — thin title
                        // bars (e.g. TitleBg, 20px tall) and small panels kept their YAML
                        // colour under the old size gate. The size now only gates the border.
                        StylePanel(img);
                    }
                    imgCount++;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[SandboxUIThemer] failed to style image '{Path(img.transform)}': {e.Message}");
                }
            }

            var texts = GetComponentsInChildren<Text>(true);
            foreach (var text in texts)
            {
                if (IsOwnedByTheme(text.transform)) continue;
                try
                {
                    StyleText(text);
                    textCount++;
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[SandboxUIThemer] failed to style text '{Path(text.transform)}': {e.Message}");
                }
            }

            Debug.Log($"[SandboxUIThemer] styled {imgCount} images, {textCount} texts.");
        }

        // Builds a "Parent/Child" hierarchy path for diagnostics.
        private static string Path(Transform t)
        {
            return t.parent != null ? t.parent.name + "/" + t.name : t.name;
        }

        private void StylePanel(Image img)
        {
            // Keep the panel's original opacity (clamped to the glass max) so faint
            // container backers stay faint instead of becoming opaque phantom pills.
            float origAlpha = img.color.a;
            float alpha = Mathf.Min(origAlpha, SandboxUITheme.PanelAlpha);

            img.sprite = UITextureFactory.RoundedFillSprite;
            img.type = Image.Type.Sliced;
            img.preserveAspect = false; // our glass sprite must fill the rect, not letterbox
            img.color = SandboxUITheme.WithAlpha(SandboxUITheme.PanelFill, alpha);

            // Only solid, reasonably-sized cards get the glowing border — thin bars and
            // tiny panels recolor but stay borderless.
            if (origAlpha >= SolidPanelAlpha && IsPanelSized(img.rectTransform))
            {
                AddBorder(img.rectTransform, SandboxUITheme.BorderRest);
            }
        }

        private void StyleButton(Image img, Selectable selectable)
        {
            img.sprite = UITextureFactory.RoundedFillSprite;
            img.type = Image.Type.Sliced;
            img.preserveAspect = false; // fixes wide buttons collapsing to a square
            img.color = SandboxUITheme.WithAlpha(SandboxUITheme.ButtonFill, SandboxUITheme.ButtonAlpha);

            // Neutralise colour-tint transition so the glass colour shows, with a gentle
            // brighten on hover/press for feedback.
            selectable.transition = Selectable.Transition.ColorTint;
            var cb = selectable.colors;
            cb.colorMultiplier = 1f;
            cb.fadeDuration = 0.1f;
            cb.normalColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            cb.highlightedColor = Color.white;
            cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            cb.selectedColor = Color.white;
            cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            selectable.colors = cb;

            AddBorder(img.rectTransform, SandboxUITheme.BorderRest);
        }

        private void StyleText(Text text)
        {
            Color before = text.color;

            // Small + uppercase reads as a label/badge → mono + muted. Otherwise body sans.
            bool isLabel = text.fontSize <= 16 && IsMostlyUpper(text.text);
            Font font = isLabel ? SandboxUITheme.Mono : SandboxUITheme.Sans;
            if (font != null) text.font = font;

            // Only override near-default (black/white/grey) colours so deliberately
            // coloured labels (values, warnings) keep their colour.
            if (IsNeutral(text.color))
            {
                text.color = isLabel ? SandboxUITheme.TextMuted : SandboxUITheme.TextPrimary;
            }

            // Diagnostic for the invisible title-text issue: log how each *Title text was
            // handled (font assigned, colour before/after, active state).
            if (text.transform.parent != null &&
                text.transform.parent.name.IndexOf("Title", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Debug.Log($"[SandboxUIThemer] title text '{Path(text.transform)}' text='{text.text}' " +
                          $"active={text.isActiveAndEnabled} font={(font != null ? font.name : "NULL")} " +
                          $"size={text.fontSize} colorBefore={before} colorAfter={text.color}");
            }
        }

        // --- border overlay -------------------------------------------------------
        private void AddBorder(RectTransform parent, Color color)
        {
            var go = new GameObject("__ThemeBorder", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.SetAsLastSibling();

            var border = go.GetComponent<Image>();
            border.sprite = UITextureFactory.RoundedBorderSprite;
            border.type = Image.Type.Sliced;
            border.color = color;
            border.raycastTarget = false;
            borderGraphics.Add(border);
        }

        // --- background (opt-in) --------------------------------------------------
        private void BuildBackground()
        {
            var canvas = GetComponent<RectTransform>();

            // Solid fill (bottom), grid (above it), glow (above grid) — all below the
            // existing UI. New children spawn at the end of the sibling list, so push each
            // to an explicit low index to sit behind every panel.
            var bg = MakeFullScreenChild("__ThemeBackground");
            var solid = bg.gameObject.AddComponent<Image>();
            solid.color = SandboxUITheme.Background;
            solid.raycastTarget = false;

            var gridGo = MakeFullScreenChild("__ThemeGrid");
            var grid = gridGo.gameObject.AddComponent<RawImage>();
            grid.texture = UITextureFactory.GridTexture;
            grid.color = Color.white;
            grid.raycastTarget = false;
            float cell = 48f;
            grid.uvRect = new Rect(0, 0, Screen.width / cell, Screen.height / cell);

            var glowGo = new GameObject("__ThemeGlow", typeof(RectTransform), typeof(RawImage));
            var grt = (RectTransform)glowGo.transform;
            grt.SetParent(canvas, false);
            grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.5f);
            grt.pivot = new Vector2(0.5f, 0.5f);
            grt.sizeDelta = new Vector2(Screen.height * 1.2f, Screen.height * 1.2f);
            glowImage = glowGo.GetComponent<RawImage>();
            glowImage.texture = UITextureFactory.GlowTexture;
            glowImage.raycastTarget = false;

            // Order them behind the UI: solid → grid → glow at indices 0,1,2.
            solid.transform.SetSiblingIndex(0);
            grid.transform.SetSiblingIndex(1);
            glowImage.transform.SetSiblingIndex(2);
        }

        private RectTransform MakeFullScreenChild(string goName)
        {
            var go = new GameObject(goName, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(GetComponent<RectTransform>(), false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return rt;
        }

        // --- accent -----------------------------------------------------------------
        private void OnModeChanged(SandboxSimType sim)
        {
            Color accent = SandboxUITheme.AccentFor(sim);

            // Subtle accent tint on all glass borders, so the whole UI shifts with the
            // active simulation without per-button mapping.
            Color borderColor = SandboxUITheme.WithAlpha(accent, 0.35f);
            for (int i = borderGraphics.Count - 1; i >= 0; i--)
            {
                if (borderGraphics[i] == null) { borderGraphics.RemoveAt(i); continue; }
                borderGraphics[i].color = borderColor;
            }

            if (glowImage != null)
            {
                glowImage.color = SandboxUITheme.WithAlpha(accent, 0.22f);
            }
        }

        // --- helpers ----------------------------------------------------------------
        private static bool IsOwnedByTheme(Transform t)
        {
            return t.name.StartsWith("__Theme");
        }

        // An Image is a themable background when it's a visible plain panel/button bg:
        // not invisible, not a structural viewport/mask, and not drawing custom art.
        private static bool IsThemableBackground(Image img)
        {
            if (img.color.a < 0.08f) return false; // invisible spacer / raycast target

            string goName = img.gameObject.name.ToLowerInvariant();
            foreach (var frag in StructuralNameFragments)
            {
                if (goName.Contains(frag)) return false;
            }

            // Slider/scrollbar fills + handles must keep their own colour or they vanish
            // into the navy track and become unreadable.
            if (IsSliderOrScrollPart(img)) return false;

            var sprite = img.sprite;
            if (sprite != null)
            {
                string spriteName = sprite.name.ToLowerInvariant();
                foreach (var frag in ArtSpriteFragments)
                {
                    if (spriteName.Contains(frag)) return false; // custom art — preserve
                }
            }
            return true;
        }

        private static bool IsSliderOrScrollPart(Image img)
        {
            var slider = img.GetComponentInParent<Slider>(true);
            if (slider != null)
            {
                if (IsSelfOrChildOf(img.transform, slider.fillRect)) return true;
                if (IsSelfOrChildOf(img.transform, slider.handleRect)) return true;
            }
            var scrollbar = img.GetComponentInParent<Scrollbar>(true);
            if (scrollbar != null && IsSelfOrChildOf(img.transform, scrollbar.handleRect)) return true;
            return false;
        }

        private static bool IsSelfOrChildOf(Transform t, RectTransform target)
        {
            return target != null && (t == target || t.IsChildOf(target));
        }

        private static bool IsPanelSized(RectTransform rt)
        {
            Rect r = rt.rect;
            return Mathf.Min(r.width, r.height) >= PanelMinSize;
        }

        private static bool IsMostlyUpper(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            int letters = 0, upper = 0;
            foreach (char c in s)
            {
                if (char.IsLetter(c)) { letters++; if (char.IsUpper(c)) upper++; }
            }
            return letters > 0 && upper == letters;
        }

        private static bool IsNeutral(Color c)
        {
            // grey-ish: low colour spread between channels.
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            return (max - min) < 0.12f;
        }
    }
}
