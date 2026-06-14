//
//  SandboxUITheme.cs
//
//  Central design system for the AR Sandbox UI refresh (dark glassmorphism).
//  All palette, accent, typography and shape constants live here so the look can
//  be tuned in one place instead of being scattered through the scene or per-panel
//  C#. Nothing here touches the scene directly — SandboxUIThemer reads these values
//  and applies them at runtime, so the whole refresh is reversible by removing the
//  Theme/ scripts.
//

using System.Collections.Generic;
using UnityEngine;

namespace ARSandbox.UI
{
    // The five sandbox simulations plus a neutral default. Drives the per-simulation
    // accent colour used for active-button glow, panel borders and the background glow.
    public enum SandboxSimType
    {
        None,
        Water,
        Fire,
        Geology,
        Wind,
        Topography,
    }

    public static class SandboxUITheme
    {
        // ----- Base palette (deep navy + coral) -----------------------------------
        public static readonly Color Background    = Hex("#0A0F1F"); // darkest — behind everything
        public static readonly Color PanelFill     = Hex("#1B2440"); // glass card fill (alpha applied below)
        public static readonly Color ButtonFill    = Hex("#28335A"); // raised control fill
        public static readonly Color TextPrimary   = Hex("#F5F7FA"); // headings / body
        public static readonly Color TextMuted      = Hex("#8A93A8"); // labels, captions
        public static readonly Color DefaultAccent = Hex("#FF6B5B"); // coral — used when no sim is active

        // Glass translucency. Panels sit at ~0.7 alpha over the grid background so the
        // ambient grid + glow read faintly through them, selling the "frosted" look.
        public const float PanelAlpha  = 0.72f;
        public const float ButtonAlpha = 0.62f;

        // Border brightness (white) for the resting glass edge; the accent overrides this
        // on active/hovered elements.
        public static readonly Color BorderRest = new Color(1f, 1f, 1f, 0.10f);

        // ----- Shape --------------------------------------------------------------
        // Corner radius (px) of the generated rounded-rect glass sprite, and the border
        // thickness baked into it. 9-slicing keeps these crisp at any panel size.
        public const int CornerRadius   = 16;
        public const int BorderThickness = 2;

        // ----- Per-simulation accents ---------------------------------------------
        private static readonly Dictionary<SandboxSimType, Color> Accents = new Dictionary<SandboxSimType, Color>
        {
            { SandboxSimType.None,       Hex("#FF6B5B") }, // coral
            { SandboxSimType.Water,      Hex("#38BDF8") }, // sky blue
            { SandboxSimType.Fire,       Hex("#FB923C") }, // orange
            { SandboxSimType.Geology,    Hex("#34D399") }, // emerald
            { SandboxSimType.Wind,       Hex("#2DD4BF") }, // teal
            { SandboxSimType.Topography, Hex("#A78BFA") }, // violet
        };

        public static Color AccentFor(SandboxSimType sim)
        {
            return Accents.TryGetValue(sim, out Color c) ? c : DefaultAccent;
        }

        // ----- Typography ---------------------------------------------------------
        // Loaded lazily from Resources/Fonts so no inspector wiring is needed. Outfit is
        // the geometric sans for headings/body; JetBrains Mono is for the small uppercase
        // labels and badges that give the techy/scientific feel.
        private const string OutfitResource = "Fonts/Outfit-Regular";
        private const string MonoResource   = "Fonts/JetBrainsMono-Regular";

        private static Font _sans;
        private static Font _mono;

        public static Font Sans
        {
            get
            {
                if (_sans == null) _sans = Resources.Load<Font>(OutfitResource);
                return _sans;
            }
        }

        public static Font Mono
        {
            get
            {
                if (_mono == null) _mono = Resources.Load<Font>(MonoResource);
                return _mono;
            }
        }

        // ----- Helpers ------------------------------------------------------------
        public static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }

        // Parses "#RRGGBB" / "#RRGGBBAA". Falls back to magenta so a typo is obvious
        // rather than silent.
        public static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.magenta;
        }
    }
}
