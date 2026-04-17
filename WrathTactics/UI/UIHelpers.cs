using Kingmaker;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WrathTactics.UI {
    static class UIHelpers {
        public static Transform StaticRoot => Game.Instance.UI.Canvas.transform;
        public static Transform ServiceWindow => StaticRoot.Find("ServiceWindowsPCView");

        public static void SetAnchor(this RectTransform transform, double xMin, double xMax, double yMin, double yMax) {
            transform.anchorMin = new Vector2((float)xMin, (float)yMin);
            transform.anchorMax = new Vector2((float)xMax, (float)yMax);
        }

        public static RectTransform Rect(this GameObject obj) => obj.transform as RectTransform;
        public static RectTransform Rect(this Transform obj) => obj as RectTransform;

        public static void FillParent(this RectTransform rect) {
            rect.SetAnchor(0, 1, 0, 1);
            rect.sizeDelta = Vector2.zero;
        }

        public static void FillParent(this GameObject obj) => obj.Rect().FillParent();

        public static (GameObject, RectTransform) Create(string name, Transform parent = null) {
            var obj = new GameObject(name, typeof(RectTransform));
            if (parent != null)
                obj.AddTo(parent);
            return (obj, obj.Rect());
        }

        public static void AddTo(this GameObject obj, Transform parent) {
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = Vector3.zero;
            obj.transform.localScale = Vector3.one;
            obj.transform.localRotation = Quaternion.identity;
        }

        public static Transform ChildTransform(this GameObject obj, string path) {
            return obj.transform.Find(path);
        }

        public static GameObject ChildObject(this GameObject obj, string path) {
            return obj.ChildTransform(path)?.gameObject;
        }

        public static T MakeComponent<T>(this GameObject obj, Action<T> build) where T : Component {
            var component = obj.AddComponent<T>();
            build(component);
            return component;
        }

        public static TextMeshProUGUI AddLabel(GameObject parent, string text, float fontSize = 20f,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, Color? color = null) {
            var (labelObj, labelRect) = Create("Label", parent.transform);
            labelRect.FillParent();
            var tmp = labelObj.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = alignment;
            tmp.color = color ?? Color.white;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            return tmp;
        }

        public static Image AddBackground(GameObject obj, Color color) {
            var img = obj.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = true;
            return img;
        }

        public static GameObject MakeButton(Transform parent, string name, string label, float fontSize,
            Color bgColor, UnityEngine.Events.UnityAction onClick) {
            var (btn, btnRect) = Create(name, parent);
            AddBackground(btn, bgColor);
            AddLabel(btn, label, fontSize, TextAlignmentOptions.Midline);
            btn.AddComponent<Button>().onClick.AddListener(onClick);
            return btn;
        }

        /// <summary>
        /// Creates a TMP_InputField with proper text viewport setup.
        /// </summary>
        public static TMP_InputField CreateTMPInputField(GameObject parent, string name,
            double xMin, double xMax, string initialText, float fontSize = 16f,
            TMP_InputField.ContentType contentType = TMP_InputField.ContentType.Standard) {

            var (obj, rect) = Create(name, parent.transform);
            rect.SetAnchor(xMin, xMax, 0, 1);
            rect.sizeDelta = Vector2.zero;
            AddBackground(obj, new Color(0.12f, 0.12f, 0.12f, 1f));

            // Text viewport (clip area with small padding)
            var (viewport, viewportRect) = Create("TextArea", obj.transform);
            viewportRect.FillParent();
            viewportRect.offsetMin = new Vector2(4, 0);
            viewportRect.offsetMax = new Vector2(-4, 0);
            viewport.AddComponent<RectMask2D>();

            // Text component
            var (textObj, textRect) = Create("Text", viewport.transform);
            textRect.FillParent();
            var textTmp = textObj.AddComponent<TextMeshProUGUI>();
            textTmp.fontSize = fontSize;
            textTmp.alignment = TextAlignmentOptions.MidlineLeft;
            textTmp.color = Color.white;
            textTmp.enableWordWrapping = false;
            textTmp.overflowMode = TextOverflowModes.Ellipsis;

            var inputField = obj.AddComponent<TMP_InputField>();
            inputField.textViewport = viewportRect;
            inputField.textComponent = textTmp;
            inputField.text = initialText;
            inputField.contentType = contentType;

            // Clicking into a field should place the cursor at the click point,
            // not select-all. Select-all means backspace wipes the whole text.
            inputField.onFocusSelectAll = false;

            // Built-in TMP caret doesn't render reliably in our custom input fields
            // (suspected font-material issue with the game's TMP setup). Attach a
            // ManualInputCaret component that renders its own blinking Image caret
            // at the real caret position.
            var manual = obj.AddComponent<ManualInputCaret>();
            manual.Init(inputField, textTmp, textRect);

            // Set the background image as the target graphic for click detection
            var bgImage = obj.GetComponent<Image>();
            if (bgImage != null) inputField.targetGraphic = bgImage;

            // Force the text component to update
            textTmp.text = initialText;

            return inputField;
        }
    }

    /// <summary>
    /// Renders a blinking "|" caret at the real TMP_InputField caret position
    /// while the field is focused. Works around cases where TMP's built-in caret
    /// doesn't render in modded input fields due to font-material quirks.
    /// Position is derived from TMP_Text.textInfo.characterInfo[i].xAdvance, so
    /// arrow keys / clicks / home/end all track correctly.
    /// </summary>
    public class ManualInputCaret : MonoBehaviour {
        TMP_InputField field;
        TextMeshProUGUI textComponent;
        RectTransform textRect;
        Image caretImage;
        RectTransform caretRect;
        float blinkTimer;
        bool caretShown = true;

        public void Init(TMP_InputField field, TextMeshProUGUI text, RectTransform textRect) {
            this.field = field;
            this.textComponent = text;
            this.textRect = textRect;
        }

        void Start() {
            if (textRect == null) return;
            var caretObj = new GameObject("ManualCaret", typeof(RectTransform));
            caretObj.transform.SetParent(textRect, false);
            caretRect = (RectTransform)caretObj.transform;
            caretRect.anchorMin = new Vector2(0, 0.15f);
            caretRect.anchorMax = new Vector2(0, 0.85f);
            caretRect.pivot = new Vector2(0, 0.5f);
            caretRect.sizeDelta = new Vector2(2, 0);
            caretRect.anchoredPosition = Vector2.zero;
            caretImage = caretObj.AddComponent<Image>();
            caretImage.color = Color.white;
            caretImage.raycastTarget = false;
            caretImage.enabled = false;
        }

        void Update() {
            if (field == null || caretImage == null || textComponent == null) return;
            if (!field.isFocused) {
                if (caretImage.enabled) caretImage.enabled = false;
                return;
            }

            caretRect.anchoredPosition = new Vector2(GetCaretX(), 0);

            blinkTimer += Time.unscaledDeltaTime;
            if (blinkTimer >= 0.53f) {
                blinkTimer = 0;
                caretShown = !caretShown;
            }
            caretImage.enabled = caretShown;
        }

        float GetCaretX() {
            int idx = field.caretPosition;
            if (idx <= 0) return 2f;

            textComponent.ForceMeshUpdate();
            var info = textComponent.textInfo;
            if (info == null || info.characterInfo == null) return 2f;

            // Clamp to the last rendered character; textInfo.characterCount is the
            // number of characters the mesh actually holds (0-based indices).
            int lastIdx = System.Math.Min(idx, info.characterCount) - 1;
            if (lastIdx < 0 || lastIdx >= info.characterInfo.Length) return 2f;

            return info.characterInfo[lastIdx].xAdvance + 2f;
        }
    }

    /// <summary>
    /// Replaces all Dropdown usage. Shows a button with current selection text.
    /// On click, creates a scrollable popup overlay on the main canvas.
    /// </summary>
    public class PopupSelector : MonoBehaviour {
        List<string> options = new List<string>();
        List<Sprite> icons; // parallel to options, may contain nulls
        int selectedIndex;
        Action<int> onSelected;
        TextMeshProUGUI buttonLabel;
        GameObject popupOverlay;

        public int SelectedIndex => selectedIndex;
        public string SelectedOption => selectedIndex >= 0 && selectedIndex < options.Count
            ? options[selectedIndex] : "";

        public static PopupSelector Create(GameObject parent, string name,
            float xMin, float xMax, List<string> options, int initialIndex,
            Action<int> onSelected) {
            return CreateWithIcons(parent, name, xMin, xMax, options, null, initialIndex, onSelected);
        }

        public static PopupSelector CreateWithIcons(GameObject parent, string name,
            float xMin, float xMax, List<string> options, List<Sprite> icons,
            int initialIndex, Action<int> onSelected) {

            var (obj, rect) = UIHelpers.Create(name, parent.transform);
            rect.SetAnchor(xMin, xMax, 0, 1);
            rect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(obj, new Color(0.22f, 0.22f, 0.22f, 1f));

            var selector = obj.AddComponent<PopupSelector>();
            selector.options = options ?? new List<string>();
            selector.icons = icons;
            selector.selectedIndex = Mathf.Clamp(initialIndex, 0,
                Mathf.Max(0, (options?.Count ?? 1) - 1));
            selector.onSelected = onSelected;

            // Button label showing current selection
            string labelText = selector.selectedIndex < selector.options.Count
                ? selector.options[selector.selectedIndex] : "";
            selector.buttonLabel = UIHelpers.AddLabel(obj, labelText, 15f,
                TextAlignmentOptions.MidlineLeft);

            // Arrow indicator on the right
            var (arrow, arrowRect) = UIHelpers.Create("Arrow", obj.transform);
            arrowRect.SetAnchor(0.88, 1, 0, 1);
            arrowRect.sizeDelta = Vector2.zero;
            UIHelpers.AddLabel(arrow, "v", 14f, TextAlignmentOptions.Midline,
                new Color(0.6f, 0.6f, 0.6f));

            // Click handler on button
            obj.AddComponent<Button>().onClick.AddListener(selector.TogglePopup);

            return selector;
        }

        public void SetOptions(List<string> newOptions, int newIndex, List<Sprite> newIcons = null) {
            options = newOptions ?? new List<string>();
            icons = newIcons;
            selectedIndex = Mathf.Clamp(newIndex, 0, Mathf.Max(0, options.Count - 1));
            UpdateLabel();
            ClosePopup();
        }

        void UpdateLabel() {
            if (buttonLabel == null) return;
            buttonLabel.text = selectedIndex >= 0 && selectedIndex < options.Count
                ? options[selectedIndex] : "";
        }

        void TogglePopup() {
            if (popupOverlay != null) {
                ClosePopup();
            } else {
                OpenPopup();
            }
        }

        void OpenPopup() {
            if (options.Count == 0) return;
            popupOverlay = CreatePickerOverlay(options, icons, selectedIndex, idx => {
                SelectOption(idx);
            });
            // Wire the overlay background click to ClosePopup so popupOverlay is nulled properly
            popupOverlay.GetComponent<Button>().onClick.AddListener(ClosePopup);
        }

        /// <summary>
        /// Creates a transient centered picker popup without a backing selector button.
        /// The overlay is destroyed when the user picks an option or clicks outside.
        /// </summary>
        public static void ShowPicker(List<string> options, Action<int> onPick) {
            GameObject overlay = null;
            overlay = CreatePickerOverlay(options, null, -1, idx => {
                onPick?.Invoke(idx);
                if (overlay != null) UnityEngine.Object.Destroy(overlay);
            });
            var capturedOverlay = overlay;
            overlay.GetComponent<Button>().onClick.AddListener(() => {
                if (capturedOverlay != null) UnityEngine.Object.Destroy(capturedOverlay);
            });
            // Attach an Escape handler that destroys the overlay — instance PopupSelectors
            // listen on their own Update, transient pickers need their own.
            overlay.AddComponent<EscapeCloser>();
        }

        /// <summary>
        /// Builds a full-screen overlay with a centered scrollable option list.
        /// Returns the overlay GameObject. The overlay has a Button component on its root
        /// with no listeners yet — callers must attach their own outside-click handler.
        /// onOptionClicked fires with the chosen index when an option button is pressed;
        /// the caller is responsible for closing/destroying the overlay.
        /// </summary>
        static GameObject CreatePickerOverlay(List<string> options, List<Sprite> icons,
            int selectedIndex, Action<int> onOptionClicked) {

            var canvas = Game.Instance.UI.Canvas.transform;

            // Full-screen overlay to catch clicks outside
            var (overlay, overlayRect) = UIHelpers.Create("PopupOverlay", canvas);
            overlayRect.FillParent();
            UIHelpers.AddBackground(overlay, new Color(0, 0, 0, 0.3f));
            // Button with no listeners yet; callers attach their outside-click handler
            overlay.AddComponent<Button>();

            // Popup container — centered on screen
            var (popup, popupRect) = UIHelpers.Create("PopupList", overlay.transform);
            UIHelpers.AddBackground(popup, new Color(0.15f, 0.15f, 0.15f, 0.98f));

            float popupWidth = 350f;
            float maxPopupHeight = 400f;
            float itemHeight = 36f;
            float totalHeight = Mathf.Min(options.Count * itemHeight + 8f, maxPopupHeight);

            popupRect.anchorMin = new Vector2(0.5f, 0.5f);
            popupRect.anchorMax = new Vector2(0.5f, 0.5f);
            popupRect.pivot = new Vector2(0.5f, 0.5f);
            popupRect.anchoredPosition = Vector2.zero;
            popupRect.sizeDelta = new Vector2(popupWidth, totalHeight);

            // Scroll view
            var (scrollObj, scrollRect) = UIHelpers.Create("Scroll", popup.transform);
            scrollRect.FillParent();

            var (viewport, viewportRect) = UIHelpers.Create("Viewport", scrollObj.transform);
            viewportRect.FillParent();
            viewport.AddComponent<RectMask2D>();

            var (content, contentRect) = UIHelpers.Create("Content", viewport.transform);
            contentRect.SetAnchor(0, 1, 1, 1);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(0, 0);

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 1;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.padding = new RectOffset(2, 2, 2, 2);

            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollObj.AddComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;

            // Option buttons
            for (int i = 0; i < options.Count; i++) {
                var capturedIndex = i;
                var (itemObj, itemObjRect) = UIHelpers.Create($"Option_{i}", content.transform);
                itemObj.AddComponent<LayoutElement>().preferredHeight = itemHeight;

                var bgColor = i == selectedIndex
                    ? new Color(0.3f, 0.35f, 0.45f, 1f)
                    : new Color(0.2f, 0.2f, 0.2f, 1f);
                UIHelpers.AddBackground(itemObj, bgColor);
                var label = UIHelpers.AddLabel(itemObj, options[i], 16f,
                    TextAlignmentOptions.MidlineLeft);

                if (icons != null && i < icons.Count && icons[i] != null) {
                    var (iconObj, iconRect) = UIHelpers.Create("Icon", itemObj.transform);
                    iconRect.anchorMin = new Vector2(0, 0.5f);
                    iconRect.anchorMax = new Vector2(0, 0.5f);
                    iconRect.pivot = new Vector2(0, 0.5f);
                    iconRect.anchoredPosition = new Vector2(4, 0);
                    iconRect.sizeDelta = new Vector2(30, 30);
                    var iconImg = iconObj.AddComponent<Image>();
                    iconImg.sprite = icons[i];
                    iconImg.preserveAspect = true;
                    iconImg.raycastTarget = false;
                    label.margin = new Vector4(38, 0, 4, 0);
                } else {
                    label.margin = new Vector4(4, 0, 4, 0);
                }

                itemObj.AddComponent<Button>().onClick.AddListener(() => {
                    onOptionClicked?.Invoke(capturedIndex);
                });
            }

            return overlay;
        }

        void SelectOption(int index) {
            selectedIndex = index;
            UpdateLabel();
            ClosePopup();
            onSelected?.Invoke(index);
        }

        void ClosePopup() {
            if (popupOverlay != null) {
                Destroy(popupOverlay);
                popupOverlay = null;
            }
        }

        void Update() {
            // Close popup on Escape
            if (popupOverlay != null && Input.GetKeyDown(KeyCode.Escape)) {
                ClosePopup();
            }
        }

        void OnDestroy() {
            ClosePopup();
        }
    }

    /// <summary>Attached to transient ShowPicker overlays so Escape destroys them.</summary>
    class EscapeCloser : MonoBehaviour {
        void Update() {
            if (Input.GetKeyDown(KeyCode.Escape))
                Destroy(gameObject);
        }
    }
}
