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

        public static TextMeshProUGUI AddLabel(GameObject parent, string text, float fontSize = 14f,
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
            double xMin, double xMax, string initialText, float fontSize = 12f,
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

            // Force the text component to update
            textTmp.text = initialText;

            return inputField;
        }
    }

    /// <summary>
    /// Replaces all Dropdown usage. Shows a button with current selection text.
    /// On click, creates a scrollable popup overlay on the main canvas.
    /// </summary>
    public class PopupSelector : MonoBehaviour {
        List<string> options = new List<string>();
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

            var (obj, rect) = UIHelpers.Create(name, parent.transform);
            rect.SetAnchor(xMin, xMax, 0, 1);
            rect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(obj, new Color(0.22f, 0.22f, 0.22f, 1f));

            var selector = obj.AddComponent<PopupSelector>();
            selector.options = options ?? new List<string>();
            selector.selectedIndex = Mathf.Clamp(initialIndex, 0,
                Mathf.Max(0, (options?.Count ?? 1) - 1));
            selector.onSelected = onSelected;

            // Button label showing current selection
            string labelText = selector.selectedIndex < selector.options.Count
                ? selector.options[selector.selectedIndex] : "";
            selector.buttonLabel = UIHelpers.AddLabel(obj, labelText, 11f,
                TextAlignmentOptions.MidlineLeft);

            // Arrow indicator on the right
            var (arrow, arrowRect) = UIHelpers.Create("Arrow", obj.transform);
            arrowRect.SetAnchor(0.88, 1, 0, 1);
            arrowRect.sizeDelta = Vector2.zero;
            UIHelpers.AddLabel(arrow, "v", 10f, TextAlignmentOptions.Midline,
                new Color(0.6f, 0.6f, 0.6f));

            // Click handler on button
            obj.AddComponent<Button>().onClick.AddListener(selector.TogglePopup);

            return selector;
        }

        public void SetOptions(List<string> newOptions, int newIndex) {
            options = newOptions ?? new List<string>();
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

            var canvas = Game.Instance.UI.Canvas.transform;

            // Full-screen overlay to catch clicks outside
            var (overlay, overlayRect) = UIHelpers.Create("PopupOverlay", canvas);
            popupOverlay = overlay;
            overlayRect.FillParent();
            var overlayImg = UIHelpers.AddBackground(overlay, new Color(0, 0, 0, 0.3f));
            overlay.AddComponent<Button>().onClick.AddListener(ClosePopup);

            // Popup container positioned near the selector button
            var (popup, popupRect) = UIHelpers.Create("PopupList", overlay.transform);
            UIHelpers.AddBackground(popup, new Color(0.15f, 0.15f, 0.15f, 0.98f));

            // Position popup at button's world position
            var buttonRect = gameObject.Rect();
            Vector3[] corners = new Vector3[4];
            buttonRect.GetWorldCorners(corners);

            // Convert to canvas local position
            var canvasRect = canvas as RectTransform;
            Vector2 localMin, localMax;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, RectTransformUtility.WorldToScreenPoint(null, corners[0]),
                null, out localMin);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, RectTransformUtility.WorldToScreenPoint(null, corners[2]),
                null, out localMax);

            float popupWidth = localMax.x - localMin.x;
            float maxPopupHeight = 300f;
            float itemHeight = 28f;
            float totalHeight = Mathf.Min(options.Count * itemHeight + 8f, maxPopupHeight);

            popupRect.anchorMin = new Vector2(0.5f, 0.5f);
            popupRect.anchorMax = new Vector2(0.5f, 0.5f);
            popupRect.pivot = new Vector2(0f, 1f);
            popupRect.anchoredPosition = new Vector2(localMin.x, localMin.y);
            popupRect.sizeDelta = new Vector2(Mathf.Max(popupWidth, 150f), totalHeight);

            // Scroll view for the options
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

            // Create option buttons
            for (int i = 0; i < options.Count; i++) {
                var capturedIndex = i;
                var (itemObj, itemObjRect) = UIHelpers.Create($"Option_{i}", content.transform);
                itemObj.AddComponent<LayoutElement>().preferredHeight = itemHeight;

                var bgColor = i == selectedIndex
                    ? new Color(0.3f, 0.35f, 0.45f, 1f)
                    : new Color(0.2f, 0.2f, 0.2f, 1f);
                UIHelpers.AddBackground(itemObj, bgColor);
                var label = UIHelpers.AddLabel(itemObj, options[i], 11f,
                    TextAlignmentOptions.MidlineLeft);
                label.margin = new Vector4(4, 0, 4, 0);

                itemObj.AddComponent<Button>().onClick.AddListener(() => {
                    SelectOption(capturedIndex);
                });
            }
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
}
