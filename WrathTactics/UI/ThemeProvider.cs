using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Engine;
using WrathTactics.Logging;

namespace WrathTactics.UI {
    public static class ThemeProvider {
        public static Sprite PanelBackground { get; private set; }
        public static Sprite InnerParchment { get; private set; }
        public static Sprite TitleBarBackground { get; private set; }
        public static Sprite CloseButtonNormal { get; private set; }
        public static Sprite CloseButtonHover { get; private set; }
        public static Sprite CloseButtonPressed { get; private set; }
        public static Sprite ActionButtonNormal { get; private set; }
        public static Sprite ActionButtonHover { get; private set; }
        public static Sprite ActionButtonPressed { get; private set; }
        public static Sprite TabHeaderActive { get; private set; }
        public static Sprite TabHeaderInactive { get; private set; }
        public static Sprite ScrollbarTrack { get; private set; }
        public static Sprite ScrollbarHandle { get; private set; }
        public static Sprite HudButton { get; private set; }
        public static Sprite HudButtonHover { get; private set; }
        public static Sprite BandMauve { get; private set; }
        public static Sprite BandBlue { get; private set; }
        public static Sprite PopupPaper { get; private set; }
        public static Sprite HintAnnotation { get; private set; }
        public static Sprite IconAdd { get; private set; }
        public static Sprite IconDelete { get; private set; }
        public static Sprite IconX { get; private set; }
        public static Sprite IconXHover { get; private set; }
        public static Sprite IconCheck { get; private set; }
        public static Sprite IconCheckHover { get; private set; }
        public static Sprite IconArrow { get; private set; }
        public static Sprite IconArrowHover { get; private set; }
        public static Sprite ToggleOn { get; private set; }
        public static Sprite ToggleOff { get; private set; }
        public static Sprite DividerFlourish { get; private set; }
        public static Sprite DividerLine { get; private set; }
        public static Sprite InputFrame { get; private set; }

        public static void Init() {
            // 9-slice border values are read directly from the original Owlcat sprite metadata
            // (UnityPy `m_Border`, axis order = left, bottom, right, top in pixels).
            PanelBackground    = Load("panel_background.png",     Vector4.zero);
            InnerParchment     = Load("inner_parchment.png",      Vector4.zero);
            TitleBarBackground = Load("titlebar_background.png",  new Vector4( 85,  30,  85, 30));
            CloseButtonNormal  = Load("close_button_normal.png",  Vector4.zero);
            CloseButtonHover   = Load("close_button_hover.png",   Vector4.zero);
            CloseButtonPressed = Load("close_button_pressed.png", Vector4.zero);
            ActionButtonNormal = Load("action_button_normal.png", new Vector4( 62,  35,  62, 35));
            ActionButtonHover  = Load("action_button_hover.png",  new Vector4( 62,  35,  62, 35));
            ActionButtonPressed= Load("action_button_pressed.png",new Vector4( 62,  35,  62, 35));
            TabHeaderActive    = Load("tab_header_active.png",    new Vector4( 28,  28,  20, 35));
            TabHeaderInactive  = Load("tab_header_inactive.png",  new Vector4( 28,  28,  20, 35));
            ScrollbarTrack     = Load("scrollbar_track.png",      new Vector4(  0,  79,   0, 67));
            ScrollbarHandle    = Load("scrollbar_handle.png",     new Vector4(  7,  41,   8, 46));
            HudButton          = Load("hud_button.png",           Vector4.zero);
            HudButtonHover     = Load("hud_button_hover.png",     Vector4.zero);

            // Ink-on-parchment set (spec 2026-09-26). Borders from tools/extract_sprites.py.
            BandMauve       = Load("band_mauve.png",       new Vector4(112,   0, 112,  0));
            BandBlue        = Load("band_blue.png",        new Vector4(112,   0, 112,  0));
            PopupPaper      = Load("popup_paper.png",      new Vector4(139, 101, 145, 89));
            HintAnnotation  = Load("hint_annotation.png",  new Vector4( 72,  58,  72, 67));
            IconAdd         = Load("icon_add.png",         Vector4.zero);
            IconDelete      = Load("icon_delete.png",      Vector4.zero);
            IconX           = Load("icon_x.png",           Vector4.zero);
            IconXHover      = Load("icon_x_hover.png",     Vector4.zero);
            IconCheck       = Load("icon_check.png",       Vector4.zero);
            IconCheckHover  = Load("icon_check_hover.png", Vector4.zero);
            IconArrow       = Load("icon_arrow.png",       Vector4.zero);
            IconArrowHover  = Load("icon_arrow_hover.png", Vector4.zero);
            ToggleOn        = Load("toggle_on.png",        Vector4.zero);
            ToggleOff       = Load("toggle_off.png",       Vector4.zero);
            DividerFlourish = Load("divider_flourish.png", new Vector4(114,   0,  94,  0));
            DividerLine     = Load("divider_line.png",     new Vector4( 16,   0,  16,  0));
            InputFrame      = Load("input_frame.png",      new Vector4( 12,  11,  13, 13));

            var all = new[] { PanelBackground, InnerParchment, TitleBarBackground,
                CloseButtonNormal, CloseButtonHover, CloseButtonPressed,
                ActionButtonNormal, ActionButtonHover, ActionButtonPressed,
                TabHeaderActive, TabHeaderInactive,
                ScrollbarTrack, ScrollbarHandle,
                HudButton, HudButtonHover,
                BandMauve, BandBlue, PopupPaper, HintAnnotation,
                IconAdd, IconDelete, IconX, IconXHover, IconCheck, IconCheckHover,
                IconArrow, IconArrowHover, ToggleOn, ToggleOff,
                DividerFlourish, DividerLine, InputFrame };
            int loaded = 0;
            foreach (var s in all) {
                if (s != null) loaded++;
            }
            Log.UI.Info($"ThemeProvider initialised — {loaded}/{all.Length} sprites loaded.");
        }

        static Sprite Load(string file, Vector4 border) =>
            AssetLoader.Load("icons", file, border);

        /// <summary>
        /// Applies the panel background sprite to obj's Image (adds Image if missing).
        /// No-op if PanelBackground is null (sprite missing from disk).
        /// </summary>
        public static void ApplyPanel(GameObject obj) {
            if (obj == null || PanelBackground == null) return;
            var img = obj.GetComponent<Image>() ?? obj.AddComponent<Image>();
            img.sprite = PanelBackground;
            // Open-book illustration (4096×2048 native, downscaled to 2048×1024 in the
            // shipped PNG). Image.Type.Simple stretches the whole sprite — paired with
            // an AspectRatioFitter on the same GameObject the book keeps its 2:1 ratio.
            img.type = Image.Type.Simple;
            img.color = Color.white;
            img.raycastTarget = true;
        }

        /// <summary>
        /// Applies the parchment / tutorial-paper sprite to obj's Image (adds Image if
        /// missing). Rendered as Simple (the whole sprite stretched to fit the rect)
        /// because the original 9-slice borders (154 px top + bottom) exceed typical
        /// rule-card heights and would produce squish artefacts under Sliced mode.
        /// No-op if InnerParchment is null.
        /// </summary>
        public static void ApplyInnerParchment(GameObject obj) {
            if (obj == null || InnerParchment == null) return;
            var img = obj.GetComponent<Image>() ?? obj.AddComponent<Image>();
            img.sprite = InnerParchment;
            img.type = Image.Type.Simple;
            img.color = Color.white;
            img.raycastTarget = true;
        }

        /// <summary>
        /// Applies a 3-state button sprite set to obj. Adds Image+Button if missing.
        /// No-op if normal sprite is null.
        /// </summary>
        public static void ApplyButton(GameObject obj, Sprite normal, Sprite hover, Sprite pressed) {
            if (obj == null || normal == null) return;
            var img = obj.GetComponent<Image>() ?? obj.AddComponent<Image>();
            img.sprite = normal;
            img.type = Image.Type.Sliced;
            img.color = Color.white;
            img.raycastTarget = true;

            var btn = obj.GetComponent<Button>() ?? obj.AddComponent<Button>();
            btn.transition = Selectable.Transition.SpriteSwap;
            btn.spriteState = new SpriteState {
                highlightedSprite = hover,
                pressedSprite     = pressed,
                selectedSprite    = hover,
                disabledSprite    = normal,
            };
            btn.targetGraphic = img;
        }

        public static void ApplyActionButton(GameObject obj) =>
            ApplyButton(obj, ActionButtonNormal, ActionButtonHover, ActionButtonPressed);

        public static void ApplyCloseButton(GameObject obj) =>
            ApplyButton(obj, CloseButtonNormal, CloseButtonHover, CloseButtonPressed);

        /// <summary>
        /// Applies a tab-header sprite. Pass `active=true` for the selected tab.
        /// Used when the tab is non-clickable (label) or paired with ApplyButton for click behaviour.
        /// </summary>
        public static void ApplyTabHeader(GameObject obj, bool active) {
            if (obj == null) return;
            var normalSprite = active ? TabHeaderActive : TabHeaderInactive;
            if (normalSprite == null) return;
            var img = obj.GetComponent<Image>() ?? obj.AddComponent<Image>();
            img.sprite = normalSprite;
            img.type = Image.Type.Sliced;
            img.color = Color.white;

            // Hover always shows the active sprite; press feedback uses the same visual.
            // If a Button is on this GO (added by AddTab), wire SpriteSwap so the hover
            // is visible — otherwise the tab would look static like before.
            var btn = obj.GetComponent<Button>();
            if (btn != null && TabHeaderActive != null) {
                btn.transition = Selectable.Transition.SpriteSwap;
                btn.spriteState = new SpriteState {
                    highlightedSprite = TabHeaderActive,
                    pressedSprite     = TabHeaderActive,
                    selectedSprite    = active ? TabHeaderActive : TabHeaderInactive,
                    disabledSprite    = TabHeaderInactive,
                };
                btn.targetGraphic = img;
            }
        }
    }
}
