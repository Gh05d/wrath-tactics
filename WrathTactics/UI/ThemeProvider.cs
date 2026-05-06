using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Engine;
using WrathTactics.Logging;

namespace WrathTactics.UI {
    public static class ThemeProvider {
        public static Sprite PanelBackground { get; private set; }
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

        public static void Init() {
            PanelBackground    = Load("panel_background.png",     new Vector2Int(32, 32), new Vector4(8, 8, 8, 8));
            TitleBarBackground = Load("titlebar_background.png",  new Vector2Int(32, 40), new Vector4(8, 8, 8, 8));
            CloseButtonNormal  = Load("close_button_normal.png",  new Vector2Int(32, 32), Vector4.zero);
            CloseButtonHover   = Load("close_button_hover.png",   new Vector2Int(32, 32), Vector4.zero);
            CloseButtonPressed = Load("close_button_pressed.png", new Vector2Int(32, 32), Vector4.zero);
            ActionButtonNormal = Load("action_button_normal.png", new Vector2Int(32, 40), new Vector4(6, 6, 6, 6));
            ActionButtonHover  = Load("action_button_hover.png",  new Vector2Int(32, 40), new Vector4(6, 6, 6, 6));
            ActionButtonPressed= Load("action_button_pressed.png",new Vector2Int(32, 40), new Vector4(6, 6, 6, 6));
            TabHeaderActive    = Load("tab_header_active.png",    new Vector2Int(24, 40), new Vector4(4, 4, 4, 4));
            TabHeaderInactive  = Load("tab_header_inactive.png",  new Vector2Int(24, 40), new Vector4(4, 4, 4, 4));
            ScrollbarTrack     = Load("scrollbar_track.png",      new Vector2Int(16,  8), new Vector4(2, 2, 2, 2));
            ScrollbarHandle    = Load("scrollbar_handle.png",     new Vector2Int(16, 16), new Vector4(2, 2, 2, 2));

            int loaded = 0;
            foreach (var s in new[] { PanelBackground, TitleBarBackground,
                CloseButtonNormal, CloseButtonHover, CloseButtonPressed,
                ActionButtonNormal, ActionButtonHover, ActionButtonPressed,
                TabHeaderActive, TabHeaderInactive,
                ScrollbarTrack, ScrollbarHandle }) {
                if (s != null) loaded++;
            }
            Log.UI.Info($"ThemeProvider initialised — {loaded}/12 sprites loaded.");
        }

        static Sprite Load(string file, Vector2Int size, Vector4 border) =>
            AssetLoader.Load("icons", file, size, border);

        /// <summary>
        /// Applies the panel background sprite to obj's Image (adds Image if missing).
        /// No-op if PanelBackground is null (sprite missing from disk).
        /// </summary>
        public static void ApplyPanel(GameObject obj) {
            if (obj == null || PanelBackground == null) return;
            var img = obj.GetComponent<Image>() ?? obj.AddComponent<Image>();
            img.sprite = PanelBackground;
            img.type = Image.Type.Sliced;
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
            var sprite = active ? TabHeaderActive : TabHeaderInactive;
            if (sprite == null) return;
            var img = obj.GetComponent<Image>() ?? obj.AddComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Sliced;
            img.color = Color.white;
        }
    }
}
