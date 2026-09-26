using UnityEngine;

namespace WrathTactics.UI {
    /// <summary>
    /// Single source of colours and metrics for the panel (spec 2026-09-26 §3.4/§3.5).
    /// Metrics are properties so they follow UIHelpers.FontScale at access time; the panel
    /// is rebuilt on a font-scale change (TacticsPanel.Toggle), so nothing caches them.
    /// No other file under UI/ may construct a Color except PackPalette.
    /// </summary>
    static class Theme {
        static Color Rgb(int r, int g, int b, float a = 1f) => new Color(r / 255f, g / 255f, b / 255f, a);

        // ---- ink and paper ----
        public static readonly Color Ink         = Rgb(0x2B, 0x1D, 0x12);
        public static readonly Color InkMuted    = Rgb(0x6B, 0x4D, 0x33);
        public static readonly Color InkLabel    = Rgb(0x4A, 0x32, 0x20);
        public static readonly Color InkFrame    = Rgb(0x3C, 0x28, 0x14, 0.55f);
        public static readonly Color InsetPaper  = Rgb(0xFF, 0xFA, 0xEE, 0.55f);
        public static readonly Color CardFill    = Rgb(0xFF, 0xFA, 0xF0, 0.18f);
        public static readonly Color ListRowFill = Rgb(0xFF, 0xFA, 0xEE, 0.35f);
        public static readonly Color BandText    = Rgb(0xF3, 0xE9, 0xD8);
        /// <summary>Soft ink outline under band text so it survives the band's translucent ends.</summary>
        public static readonly Color BandTextOutline = Rgb(0x2B, 0x1D, 0x12, 0.75f);
        public static readonly Color HintBacking = Rgb(0x30, 0x28, 0x19, 0.90f);
        public static readonly Color HintText    = Rgb(0xE9, 0xE2, 0xD0);

        // ---- status ----
        public static readonly Color StatusOk    = Rgb(0x3F, 0x6B, 0x3A);
        public static readonly Color StatusWarn  = Rgb(0x8A, 0x5A, 0x1A);
        public static readonly Color StatusError = Rgb(0x8A, 0x2A, 0x2A);
        public static readonly Color StatusMuted = InkMuted;

        // ---- HUD (outside the book) ----
        public static readonly Color BadgeOn = new Color(0.35f, 0.9f, 0.35f);

        // ---- overlays ----
        public static readonly Color DimPopup    = new Color(0f, 0f, 0f, 0.35f);
        public static readonly Color DimBackdrop = new Color(0f, 0f, 0f, 0.60f);

        // ---- flat fallbacks when a sprite failed to load ----
        public static readonly Color BandFallbackMauve = Rgb(0x6E, 0x4A, 0x6E);
        public static readonly Color BandFallbackBlue  = Rgb(0x4A, 0x55, 0x74);
        public static readonly Color PaperFallback     = Rgb(0xE5, 0xDC, 0xC8, 0.98f);
        public static readonly Color TitleFallback     = Rgb(0x33, 0x26, 0x1A);

        // ---- hover tints for ColorTint buttons ----
        public static readonly Color HoverTint    = new Color(0.85f, 0.85f, 0.85f, 1f);
        public static readonly Color PressedTint  = new Color(0.65f, 0.65f, 0.65f, 1f);
        public static readonly Color DisabledTint = new Color(0.6f, 0.6f, 0.6f, 0.6f);

        /// <summary>
        /// Image.color multiplies the band sprite, so the dark PackPalette entries would
        /// black it out. Normalise the palette colour to a max channel of 1 — the hue
        /// survives, the brightness stays the band's own.
        /// </summary>
        public static Color PackBandTint(int colorIndex) {
            var c = PackPalette.ColorAt(colorIndex);
            float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (m <= 0f) return Color.white;
            return new Color(c.r / m, c.g / m, c.b / m, 1f);
        }

        // ---- metrics (base px × FontScale) ----
        static float S => UIHelpers.FontScale;
        public static float RowHeight           => 30f * S;
        public static float HeaderHeight        => 40f * S;
        public static float CollapsedBodyHeight => 30f * S;
        public static float InlineRowHeight     => 24f * S;
        public static float DividerHeight       => 14f * S;
        public static float IconSmall           => 18f * S;
        public static float IconMedium          => 24f * S;
        /// <summary>Clears the band sprite's brush-stroke fade (≈18 px after the 2× slice multiplier in Widgets.ApplyBand).</summary>
        public static float BandPaddingX        => 26f * S;
        public static float RowGap              => 6f * S;
        public static float CardGap             => 12f * S;
        public static float CardPadding         => 8f * S;
        public static float MinCardHeight       => 160f * S;
        public static float MaxCardHeight       => 500f * S;
        public static float PopupWidth          => 480f * S;
        public static float PopupHeight         => 540f * S;
        public static float PopupListWidth      => 350f * S;
        public static float PopupListMaxHeight  => 400f * S;
        public static float PopupRowHeight      => 32f * S;
        public static float ControlRowHeight    => 36f * S;
        public static float FilterRowHeight     => 32f * S;
        public static float HintHeight          => 52f * S;
        public static float HintHeightShort     => 40f * S;
        public static float StatusHeight        => 24f * S;
        public static float SectionLabelHeight  => 20f * S;
        /// <summary>"or" label between the OR-divider lines — wide enough for "ODER" / "ИЛИ" at scale 2.</summary>
        public static float OrLabelWidth        => 56f * S;
        /// <summary>Popup content inset past the paper sprite's torn edge.</summary>
        public static float PaperInset          => 34f * S;
    }
}
