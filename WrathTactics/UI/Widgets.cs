using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace WrathTactics.UI {
    public enum BandStyle { Mauve, Blue }
    public enum Icon { Add, Delete, X, ArrowUp, ArrowDown, Chevron, Check }
    public enum Status { Ok, Warn, Error, Muted }

    /// <summary>
    /// Role-named factories for every control on the panel (spec 2026-09-26 §4.3).
    /// Together with Theme this is the only place under UI/ that touches colours or
    /// AddBackground. Every sprite path has a flat Theme fallback so a missing PNG
    /// degrades to a readable panel, never to an exception.
    /// </summary>
    static class Widgets {
        // ====================================================================
        // Surfaces
        // ====================================================================

        /// <summary>Sliced brush-band image on obj. Returns the Image (never null).</summary>
        public static Image ApplyBand(GameObject obj, BandStyle style, Color? tint = null) {
            var sprite = style == BandStyle.Blue ? ThemeProvider.BandBlue : ThemeProvider.BandMauve;
            var img = obj.GetComponent<Image>() ?? obj.AddComponent<Image>();
            img.raycastTarget = true;
            if (sprite != null) {
                img.sprite = sprite;
                img.type = Image.Type.Sliced;
                // The brush-stroke ends are 112 px 9-slice borders; at 1× they are wider than
                // the text padding and neighbouring bands overlap. 2× halves them on screen.
                img.pixelsPerUnitMultiplier = 2f;
                img.color = tint ?? Color.white;
            } else {
                var flat = style == BandStyle.Blue ? Theme.BandFallbackBlue : Theme.BandFallbackMauve;
                img.color = tint.HasValue ? flat * tint.Value : flat;
            }
            return img;
        }

        /// <summary>
        /// Full-width band row with an HLG inside (spacing RowGap, padding BandPaddingX).
        /// Children: add LayoutElements via InRow().
        /// </summary>
        public static GameObject BandRow(Transform parent, string name, BandStyle style, float height, Color? tint = null) {
            var (row, _) = UIHelpers.Create(name, parent);
            row.AddComponent<LayoutElement>().preferredHeight = height;
            ApplyBand(row, style, tint);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = Theme.RowGap;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.padding = new RectOffset((int)Theme.BandPaddingX, (int)Theme.BandPaddingX, 4, 4);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            return row;
        }

        /// <summary>Plain HLG row on the paper (no surface). Same spacing as BandRow.</summary>
        public static GameObject Row(Transform parent, string name, float height) {
            var (row, _) = UIHelpers.Create(name, parent);
            row.AddComponent<LayoutElement>().preferredHeight = height;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = Theme.RowGap;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.padding = new RectOffset(0, 0, 2, 2);
            hlg.childAlignment = TextAnchor.MiddleLeft;
            return row;
        }

        /// <summary>LayoutElement for a child of an HLG/VLG row. Proportional widths via flexibleWidth.</summary>
        public static LayoutElement InRow(GameObject obj, float preferredWidth, float flexibleWidth) {
            var le = obj.GetComponent<LayoutElement>() ?? obj.AddComponent<LayoutElement>();
            le.preferredWidth = preferredWidth;
            le.flexibleWidth = flexibleWidth;
            return le;
        }

        /// <summary>Four 1 px ink edges. Children ignore layout so the host can sit in an HLG.</summary>
        public static void AddInkFrame(GameObject obj, float thickness = 1f) {
            void Edge(string name, Vector2 aMin, Vector2 aMax, Vector2 size) {
                var (e, r) = UIHelpers.Create(name, obj.transform);
                r.anchorMin = aMin;
                r.anchorMax = aMax;
                r.sizeDelta = size;
                r.anchoredPosition = Vector2.zero;
                r.pivot = new Vector2(aMin.x == aMax.x ? aMin.x : 0.5f, aMin.y == aMax.y ? aMin.y : 0.5f);
                var img = e.AddComponent<Image>();
                img.color = Theme.InkFrame;
                img.raycastTarget = false;
                e.AddComponent<LayoutElement>().ignoreLayout = true;
            }
            Edge("FrameTop",    new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, thickness));
            Edge("FrameBottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, thickness));
            Edge("FrameLeft",   new Vector2(0, 0), new Vector2(0, 1), new Vector2(thickness, 0));
            Edge("FrameRight",  new Vector2(1, 0), new Vector2(1, 1), new Vector2(thickness, 0));
        }

        /// <summary>Paper inset (inputs, checkbox box): InsetPaper fill + ink frame.</summary>
        public static Image AddInset(GameObject obj) {
            var img = UIHelpers.AddBackground(obj, Theme.InsetPaper);
            AddInkFrame(obj);
            return img;
        }

        /// <summary>Rule card / list card surface: faint fill + ink frame.</summary>
        public static Image ApplyCard(GameObject obj) {
            var img = obj.GetComponent<Image>() ?? obj.AddComponent<Image>();
            img.sprite = null;
            img.color = Theme.CardFill;
            img.raycastTarget = true;
            AddInkFrame(obj);
            return img;
        }

        // ====================================================================
        // Text
        // ====================================================================

        public static TextMeshProUGUI InkLabel(GameObject parent, string text, float fontSize,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, Color? color = null, bool italic = false) {
            var tmp = UIHelpers.AddLabel(parent, text, fontSize, align, color ?? Theme.Ink);
            if (italic) tmp.fontStyle |= FontStyles.Italic;
            return tmp;
        }

        public static TextMeshProUGUI BandLabel(GameObject parent, string text, float fontSize,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool italic = false) {
            var tmp = UIHelpers.AddLabel(parent, text, Mathf.Max(14f, fontSize), align, Theme.BandText);
            if (italic) tmp.fontStyle |= FontStyles.Italic;
            tmp.outlineWidth = 0.15f;
            tmp.outlineColor = Theme.BandTextOutline;
            return tmp;
        }

        /// <summary>IF / THEN / TARGET / PACKS: small caps, letter-spaced, ink.</summary>
        public static TextMeshProUGUI SectionLabel(GameObject parent, string text, float fontSize = 15f) {
            var tmp = UIHelpers.AddLabel(parent, text, fontSize, TextAlignmentOptions.MidlineLeft, Theme.InkLabel);
            tmp.fontStyle = FontStyles.SmallCaps | FontStyles.Bold;
            tmp.characterSpacing = 8f;
            return tmp;
        }

        /// <summary>Full-width section label row (VLG child).</summary>
        public static GameObject SectionLabelRow(Transform parent, string name, string text) {
            var (obj, _) = UIHelpers.Create(name, parent);
            obj.AddComponent<LayoutElement>().preferredHeight = Theme.SectionLabelHeight;
            SectionLabel(obj, text);
            return obj;
        }

        public static Color StatusColor(Status kind) {
            switch (kind) {
                case Status.Ok: return Theme.StatusOk;
                case Status.Warn: return Theme.StatusWarn;
                case Status.Error: return Theme.StatusError;
                default: return Theme.StatusMuted;
            }
        }

        /// <summary>Status / empty-state line in ink, italic. Keeps the "Label" child name (PresetPanel.SetStatus finds it).</summary>
        public static TextMeshProUGUI StatusLabel(Transform parent, string name, string text, Color color,
            float preferredHeight, float fontSize = 13f) {
            var (obj, _) = UIHelpers.Create(name, parent);
            obj.AddComponent<LayoutElement>().preferredHeight = preferredHeight;
            var tmp = InkLabel(obj, text, fontSize, TextAlignmentOptions.MidlineLeft, color, italic: true);
            tmp.margin = new Vector4(6, 0, 6, 0);
            tmp.raycastTarget = true;   // wheel reaches the ScrollRect
            return tmp;
        }

        /// <summary>Journal-annotation hint card (replaces UIHelpers.AddHintCard).</summary>
        public static TextMeshProUGUI HintCard(Transform parent, string text, float preferredHeight) {
            var (obj, _) = UIHelpers.Create("Hint", parent);
            obj.AddComponent<LayoutElement>().preferredHeight = preferredHeight;
            UIHelpers.AddBackground(obj, Theme.HintBacking);
            if (ThemeProvider.HintAnnotation != null) {
                var (art, artRect) = UIHelpers.Create("Annotation", obj.transform);
                artRect.FillParent();
                var img = art.AddComponent<Image>();
                img.sprite = ThemeProvider.HintAnnotation;
                img.type = Image.Type.Sliced;
                img.color = Color.white;
                img.raycastTarget = false;
            }
            var tmp = UIHelpers.AddLabel(obj, text, 14f, TextAlignmentOptions.MidlineLeft, Theme.HintText);
            tmp.fontStyle |= FontStyles.Italic;
            tmp.enableWordWrapping = true;
            tmp.raycastTarget = true;
            tmp.margin = new Vector4(14, 6, 14, 6);
            return tmp;
        }

        // ====================================================================
        // Controls
        // ====================================================================

        static Sprite IconSprite(Icon icon, out Sprite hover, out bool rotateUp, out bool rotateDown) {
            hover = null;
            rotateUp = rotateDown = false;
            switch (icon) {
                case Icon.Add: return ThemeProvider.IconAdd;
                case Icon.Delete: return ThemeProvider.IconDelete;
                case Icon.X: hover = ThemeProvider.IconXHover; return ThemeProvider.IconX;
                case Icon.ArrowUp: hover = ThemeProvider.IconArrowHover; rotateUp = true; return ThemeProvider.IconArrow;
                case Icon.ArrowDown: hover = ThemeProvider.IconArrowHover; rotateDown = true; return ThemeProvider.IconArrow;
                case Icon.Chevron: return ThemeProvider.IconCheck;
                case Icon.Check: return ThemeProvider.IconCheckHover;
                default: return null;
            }
        }

        static string IconFallbackGlyph(Icon icon) {
            switch (icon) {
                case Icon.Add: return "+";
                case Icon.Delete: case Icon.X: return "✕";
                case Icon.ArrowUp: return "▲";
                case Icon.ArrowDown: return "▼";
                case Icon.Chevron: return "▾";
                case Icon.Check: return "✓";
                default: return "?";
            }
        }

        /// <summary>Non-interactive icon image sized `size`, aspect preserved. Falls back to an ink glyph.</summary>
        public static GameObject IconImage(Transform parent, string name, Icon icon, float size, Color? tint = null) {
            var (obj, rect) = UIHelpers.Create(name, parent);
            rect.sizeDelta = new Vector2(size, size);
            var le = obj.AddComponent<LayoutElement>();
            le.preferredWidth = size;
            le.preferredHeight = size;
            le.flexibleWidth = 0;
            var sprite = IconSprite(icon, out _, out bool up, out bool down);
            if (sprite != null) {
                var img = obj.AddComponent<Image>();
                img.sprite = sprite;
                img.preserveAspect = true;
                img.raycastTarget = false;
                img.color = tint ?? Color.white;
                if (up) rect.localRotation = Quaternion.Euler(0, 0, 90f);
                if (down) rect.localRotation = Quaternion.Euler(0, 0, -90f);
            } else {
                UIHelpers.AddLabel(obj, IconFallbackGlyph(icon), size * 0.7f / UIHelpers.FontScale,
                    TextAlignmentOptions.Midline, tint ?? Theme.Ink);
            }
            return obj;
        }

        /// <summary>Clickable icon (delete, X, arrows). SpriteSwap when a hover sprite exists, else ColorTint.</summary>
        public static GameObject IconButton(Transform parent, string name, Icon icon, float size, UnityAction onClick,
            Color? tint = null) {
            var (obj, rect) = UIHelpers.Create(name, parent);
            var le = obj.AddComponent<LayoutElement>();
            le.preferredWidth = size;
            le.preferredHeight = size;
            le.flexibleWidth = 0;
            var sprite = IconSprite(icon, out var hover, out bool up, out bool down);
            // The sketched X and arrows are pale glyphs; on paper or a band they read as ink.
            if (tint == null && (icon == Icon.X || icon == Icon.ArrowUp || icon == Icon.ArrowDown))
                tint = Theme.Ink;
            Image img;
            if (sprite != null) {
                img = obj.AddComponent<Image>();
                img.sprite = sprite;
                img.preserveAspect = true;
                img.raycastTarget = true;
                img.color = tint ?? Color.white;
                if (up) rect.localRotation = Quaternion.Euler(0, 0, 90f);
                if (down) rect.localRotation = Quaternion.Euler(0, 0, -90f);
            } else {
                img = UIHelpers.AddBackground(obj, Theme.InsetPaper);
                UIHelpers.AddLabel(obj, IconFallbackGlyph(icon), 14f, TextAlignmentOptions.Midline, Theme.Ink);
            }
            var btn = obj.AddComponent<Button>();
            btn.targetGraphic = img;
            if (hover != null && sprite != null) {
                btn.transition = Selectable.Transition.SpriteSwap;
                btn.spriteState = new SpriteState {
                    highlightedSprite = hover, pressedSprite = hover, selectedSprite = sprite, disabledSprite = sprite,
                };
            } else {
                ApplyColorTint(btn);
            }
            btn.onClick.AddListener(onClick);
            return obj;
        }

        public static void ApplyColorTint(Button btn) {
            btn.transition = Selectable.Transition.ColorTint;
            var c = btn.colors;
            c.normalColor = Color.white;
            c.highlightedColor = Theme.HoverTint;
            c.pressedColor = Theme.PressedTint;
            c.selectedColor = Color.white;
            c.disabledColor = Theme.DisabledTint;
            c.fadeDuration = 0.1f;
            btn.colors = c;
        }

        /// <summary>Round ON/OFF dot. Use SetToggleDot to flip without rebuilding.</summary>
        public static GameObject ToggleDot(Transform parent, string name, bool on, UnityAction onClick) {
            var (obj, _) = UIHelpers.Create(name, parent);
            float size = Theme.IconMedium;
            var le = obj.AddComponent<LayoutElement>();
            le.preferredWidth = size;
            le.preferredHeight = size;
            le.flexibleWidth = 0;
            var img = obj.AddComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = true;
            var btn = obj.AddComponent<Button>();
            btn.targetGraphic = img;
            ApplyColorTint(btn);
            btn.onClick.AddListener(onClick);
            SetToggleDot(obj, on);
            return obj;
        }

        public static void SetToggleDot(GameObject dot, bool on) {
            var img = dot.GetComponent<Image>();
            if (img == null) return;
            var sprite = on ? ThemeProvider.ToggleOn : ThemeProvider.ToggleOff;
            if (sprite != null) {
                img.sprite = sprite;
                img.color = on ? Color.white : new Color(1f, 1f, 1f, 0.7f);
            } else {
                img.sprite = null;
                img.color = on ? Theme.StatusOk : Theme.InkFrame;
            }
        }

        /// <summary>
        /// Italic text link, optional icon prefix. Sizes itself from the text (the TMP sits on
        /// its own child so an HLG parent reads its preferred width). onBand=true renders in BandText.
        /// </summary>
        public static GameObject InlineLink(Transform parent, string name, string text, UnityAction onClick,
            Icon? prefix = null, float fontSize = 14f, bool onBand = false) {
            var (root, _) = UIHelpers.Create(name, parent);
            var hlg = root.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4f;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.padding = new RectOffset(2, 4, 0, 0);
            if (prefix.HasValue) IconImage(root.transform, "Prefix", prefix.Value, Theme.IconSmall);

            var (txt, _t) = UIHelpers.Create("Text", root.transform);
            var tmp = txt.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize * UIHelpers.FontScale;
            tmp.fontStyle = FontStyles.Italic;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.color = onBand ? Theme.BandText : Theme.InkMuted;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = true;
            if (onBand) {
                tmp.outlineWidth = 0.15f;
                tmp.outlineColor = Theme.BandTextOutline;
            }

            // The nested HLG reported 0 width for the TMP child in the game's TMP build (every
            // link collapsed on the Deck), so the root's width is measured and pinned explicitly.
            if (tmp.font == null) tmp.font = TMP_Settings.defaultFontAsset;
            float textWidth = tmp.GetPreferredValues(text).x;
            float linkWidth = textWidth + hlg.padding.horizontal + 2f
                + (prefix.HasValue ? Theme.IconSmall + hlg.spacing : 0f);
            InRow(root, linkWidth, 0f);

            var btn = root.AddComponent<Button>();
            btn.targetGraphic = tmp;
            ApplyColorTint(btn);
            btn.onClick.AddListener(onClick);
            return root;
        }

        /// <summary>Ink-framed box with a check tick + label. onChanged receives the new value.</summary>
        public static GameObject Checkbox(Transform parent, string name, string label, bool value, Action<bool> onChanged) {
            var row = Row(parent, name, Theme.InlineRowHeight);
            // Children keep their preferred height (box stays square); the row centres them.
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            var (box, _) = UIHelpers.Create("Box", row.transform);
            float size = Theme.IconSmall;
            InRow(box, size, 0f).preferredHeight = size;
            AddInset(box);
            var tick = IconImage(box.transform, "Tick", Icon.Check, size * 0.8f);
            tick.GetComponent<LayoutElement>().ignoreLayout = true;
            var tickRect = tick.Rect();
            tickRect.anchorMin = new Vector2(0.5f, 0.5f);
            tickRect.anchorMax = new Vector2(0.5f, 0.5f);
            tickRect.anchoredPosition = Vector2.zero;
            tick.SetActive(value);

            var (lbl, _l) = UIHelpers.Create("Label", row.transform);
            InRow(lbl, 200f, 1f).flexibleHeight = 1f;
            InkLabel(lbl, label, 14f).raycastTarget = true;   // the whole row is the click target

            bool current = value;
            var btn = row.AddComponent<Button>();
            btn.targetGraphic = box.GetComponent<Image>();
            ApplyColorTint(btn);
            btn.onClick.AddListener(() => {
                current = !current;
                tick.SetActive(current);
                onChanged?.Invoke(current);
            });
            return row;
        }

        /// <summary>Owlcat action button (UI_Button_*) with band text — primary actions only.</summary>
        public static GameObject ActionButton(Transform parent, string name, string label, float fontSize,
            UnityAction onClick, float preferredWidth = 160f, float flexibleWidth = 0f) {
            var (btn, _) = UIHelpers.Create(name, parent);
            InRow(btn, preferredWidth, flexibleWidth);
            if (ThemeProvider.ActionButtonNormal != null) {
                ThemeProvider.ApplyActionButton(btn);
            } else {
                UIHelpers.AddBackground(btn, Theme.BandFallbackMauve);
                var b = btn.AddComponent<Button>();
                ApplyColorTint(b);
            }
            BandLabel(btn, label, fontSize, TextAlignmentOptions.Midline);
            btn.GetComponent<Button>().onClick.AddListener(onClick);
            return btn;
        }

        /// <summary>
        /// Band-styled dropdown trigger: [icon?] label ……… chevron. Used by PopupSelector and the
        /// spell/buff picker buttons. Caller adds the Button and its listener.
        /// </summary>
        public static GameObject BandDropdownShell(Transform parent, string name, string text, bool withIcon,
            out TextMeshProUGUI label, out Image icon) {
            var (obj, _) = UIHelpers.Create(name, parent);
            ApplyBand(obj, BandStyle.Mauve);

            icon = null;
            float leftMargin = Theme.BandPaddingX;
            if (withIcon) {
                var (iconGO, iconRect) = UIHelpers.Create("Icon", obj.transform);
                float s = Theme.IconMedium;
                iconRect.anchorMin = new Vector2(0, 0.5f);
                iconRect.anchorMax = new Vector2(0, 0.5f);
                iconRect.pivot = new Vector2(0, 0.5f);
                iconRect.anchoredPosition = new Vector2(Theme.BandPaddingX * 0.6f, 0);
                iconRect.sizeDelta = new Vector2(s, s);
                icon = iconGO.AddComponent<Image>();
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                leftMargin = Theme.BandPaddingX * 0.6f + s + 6f;
            }

            label = BandLabel(obj, text, 15f);
            label.margin = new Vector4(leftMargin, 0f, Theme.BandPaddingX + Theme.IconSmall, 0f);

            // Image.color multiplies the sprite: the grey ink glyph cannot be lightened onto the
            // band, so it is darkened to ink instead (dark-on-mauve reads; grey-on-mauve does not).
            var chev = IconImage(obj.transform, "Chevron", Icon.Chevron, Theme.IconSmall * 0.9f, Theme.Ink);
            chev.GetComponent<LayoutElement>().ignoreLayout = true;
            var cr = chev.Rect();
            cr.anchorMin = new Vector2(1, 0.5f);
            cr.anchorMax = new Vector2(1, 0.5f);
            cr.pivot = new Vector2(1, 0.5f);
            cr.anchoredPosition = new Vector2(-Theme.BandPaddingX * 0.7f, 0);
            return obj;
        }

        /// <summary>Thin line — "or" — thin line.</summary>
        public static GameObject OrDivider(Transform parent, string text) {
            var row = Row(parent, "OrDivider", Theme.DividerHeight);
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = false;
            GameObject Line(string n) {
                var (l, _) = UIHelpers.Create(n, row.transform);
                InRow(l, 0f, 1f).preferredHeight = 4f;
                var img = l.AddComponent<Image>();
                img.raycastTarget = false;
                if (ThemeProvider.DividerLine != null) {
                    img.sprite = ThemeProvider.DividerLine;
                    img.type = Image.Type.Sliced;
                    img.color = new Color(1f, 1f, 1f, 0.7f);
                } else {
                    img.color = Theme.InkFrame;
                }
                return l;
            }
            Line("LineL");
            var (lbl, _) = UIHelpers.Create("Text", row.transform);
            InRow(lbl, Theme.OrLabelWidth, 0f).flexibleHeight = 1f;
            InkLabel(lbl, text, 13f, TextAlignmentOptions.Midline, Theme.InkMuted, italic: true);
            Line("LineR");
            return row;
        }

        /// <summary>Flourish separator (between top controls and list, between IF and THEN).</summary>
        public static GameObject FlourishDivider(Transform parent) {
            var (obj, _) = UIHelpers.Create("Flourish", parent);
            obj.AddComponent<LayoutElement>().preferredHeight = Theme.DividerHeight;
            var img = obj.AddComponent<Image>();
            img.raycastTarget = false;
            if (ThemeProvider.DividerFlourish != null) {
                img.sprite = ThemeProvider.DividerFlourish;
                img.type = Image.Type.Sliced;
                img.color = new Color(1f, 1f, 1f, 0.55f);
            } else {
                img.color = new Color(0, 0, 0, 0);
            }
            return obj;
        }

        /// <summary>Popup list row: faint paper fill (so hover tints), or a band when selected.</summary>
        public static GameObject ListRow(Transform parent, string name, float height, bool selected) {
            var (row, _) = UIHelpers.Create(name, parent);
            row.AddComponent<LayoutElement>().preferredHeight = height;
            if (selected) ApplyBand(row, BandStyle.Mauve);
            else UIHelpers.AddBackground(row, Theme.ListRowFill);
            return row;
        }

        public static Color ListRowTextColor(bool selected) => selected ? Theme.BandText : Theme.Ink;

        // ====================================================================
        // Popups
        // ====================================================================

        public struct PaperPopupParts {
            public GameObject Overlay;   // full-screen dim, carries a Button (outside-click)
            public GameObject Popup;     // paper sheet, swallows clicks
            public GameObject Content;   // inset rect for the caller's layout
            public Button OverlayButton;
        }

        /// <summary>
        /// Dim + centred paper sheet + inset content rect. The caller attaches the outside-click
        /// handler to OverlayButton and its own Escape handling. Title is optional (null skips it).
        /// </summary>
        public static PaperPopupParts PaperPopup(string name, float width, float height, string title, UnityAction onClose) {
            var canvas = Kingmaker.Game.Instance.UI.Canvas.transform;
            var (overlay, overlayRect) = UIHelpers.Create(name, canvas);
            overlayRect.FillParent();
            UIHelpers.AddBackground(overlay, Theme.DimPopup);
            var overlayBtn = overlay.AddComponent<Button>();

            var (popup, popupRect) = UIHelpers.Create("Popup", overlay.transform);
            popupRect.anchorMin = new Vector2(0.5f, 0.5f);
            popupRect.anchorMax = new Vector2(0.5f, 0.5f);
            popupRect.pivot = new Vector2(0.5f, 0.5f);
            popupRect.anchoredPosition = Vector2.zero;
            popupRect.sizeDelta = new Vector2(width, height);
            var paper = popup.AddComponent<Image>();
            paper.raycastTarget = true;
            if (ThemeProvider.PopupPaper != null) {
                paper.sprite = ThemeProvider.PopupPaper;
                paper.type = Image.Type.Sliced;
                paper.color = Color.white;
            } else {
                paper.color = Theme.PaperFallback;
            }
            var swallow = popup.AddComponent<Button>();
            swallow.targetGraphic = paper;
            swallow.transition = Selectable.Transition.None;

            var (content, contentRect) = UIHelpers.Create("Content", popup.transform);
            contentRect.FillParent();
            float inset = Theme.PaperInset;
            contentRect.offsetMin = new Vector2(inset, inset);
            contentRect.offsetMax = new Vector2(-inset, -inset);

            if (title != null) {
                var (titleRow, titleRect) = UIHelpers.Create("Title", content.transform);
                // Callers may put a layout group on Content (SaveAsPack does); the title is anchored.
                titleRow.AddComponent<LayoutElement>().ignoreLayout = true;
                titleRect.anchorMin = new Vector2(0, 1);
                titleRect.anchorMax = new Vector2(1, 1);
                titleRect.pivot = new Vector2(0.5f, 1);
                titleRect.sizeDelta = new Vector2(0, Theme.RowHeight);
                titleRect.anchoredPosition = Vector2.zero;
                var t = InkLabel(titleRow, title, 18f);
                t.fontStyle = FontStyles.Bold;
                t.margin = new Vector4(0, 0, Theme.IconSmall + 8f, 0);
                var close = IconButton(titleRow.transform, "Close", Icon.X, Theme.IconSmall, onClose);
                var closeRect = close.Rect();
                closeRect.anchorMin = new Vector2(1, 0.5f);
                closeRect.anchorMax = new Vector2(1, 0.5f);
                closeRect.pivot = new Vector2(1, 0.5f);
                closeRect.anchoredPosition = Vector2.zero;
                closeRect.sizeDelta = new Vector2(Theme.IconSmall, Theme.IconSmall);
            }

            return new PaperPopupParts { Overlay = overlay, Popup = popup, Content = content, OverlayButton = overlayBtn };
        }
    }
}
