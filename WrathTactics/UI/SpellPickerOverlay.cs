using System;
using System.Collections.Generic;
using Kingmaker;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WrathTactics.UI {
    /// <summary>
    /// Search-first overlay for selecting a spell/ability entry. Takes the pre-built
    /// SpellDropdownProvider.SpellEntry list directly (entries are already scoped to
    /// a unit + action type). Mirrors BuffPickerOverlay's interaction model without
    /// the recents/defaults sections — spell lists are per-context and a global
    /// recents cache would mix results across units confusingly.
    /// </summary>
    public class SpellPickerOverlay : MonoBehaviour {
        List<SpellDropdownProvider.SpellEntry> entries;
        Action<SpellDropdownProvider.SpellEntry> onSelected;
        TMP_InputField searchInput;
        GameObject rowsContainer;
        string currentQuery = "";
        bool closed;

        public static GameObject Open(List<SpellDropdownProvider.SpellEntry> entries,
            string currentGuid,
            Action<SpellDropdownProvider.SpellEntry> onSelected) {
            var canvas = Game.Instance.UI.Canvas.transform;

            var (overlay, overlayRect) = UIHelpers.Create("SpellPickerOverlay", canvas);
            overlayRect.FillParent();
            UIHelpers.AddBackground(overlay, new Color(0, 0, 0, 0.4f));
            overlay.AddComponent<Button>().onClick.AddListener(() => Destroy(overlay));

            var (popup, popupRect) = UIHelpers.Create("Popup", overlay.transform);
            UIHelpers.AddBackground(popup, new Color(0.12f, 0.12f, 0.12f, 0.99f));
            popupRect.anchorMin = new Vector2(0.5f, 0.5f);
            popupRect.anchorMax = new Vector2(0.5f, 0.5f);
            popupRect.pivot = new Vector2(0.5f, 0.5f);
            popupRect.anchoredPosition = Vector2.zero;
            popupRect.sizeDelta = new Vector2(480f, 540f);

            // Prevent popup clicks from bubbling up and closing the overlay.
            var swallow = popup.AddComponent<Button>();
            swallow.targetGraphic = popup.GetComponent<Image>();

            var controller = popup.AddComponent<SpellPickerOverlay>();
            controller.entries = entries ?? new List<SpellDropdownProvider.SpellEntry>();
            controller.onSelected = entry => {
                if (controller.closed) return;
                controller.closed = true;
                onSelected?.Invoke(entry);
                Destroy(overlay);
            };
            controller.BuildUI(popup);
            controller.RenderList();

            return overlay;
        }

        void BuildUI(GameObject popup) {
            const float headerHeight = 52f;  // 40 felt cramped — overlay is 480x540 (BuffPicker's 420x500 used 40)

            var (header, headerRect) = UIHelpers.Create("Header", popup.transform);
            headerRect.anchorMin = new Vector2(0, 1);
            headerRect.anchorMax = new Vector2(1, 1);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0, headerHeight);
            headerRect.anchoredPosition = Vector2.zero;
            UIHelpers.AddBackground(header, new Color(0.18f, 0.18f, 0.18f, 1f));

            searchInput = UIHelpers.CreateTMPInputField(header, "Search",
                0.02, 0.98, "", 16f);
            var inputRect = searchInput.GetComponent<RectTransform>();
            // Stretch full-width within the header's 0.02-0.98 slice, inset 8px top/bottom
            // as absolute offsets (fractional y-anchors give inconsistent padding when
            // header height changes).
            inputRect.anchorMin = new Vector2(0.02f, 0f);
            inputRect.anchorMax = new Vector2(0.98f, 1f);
            inputRect.pivot = new Vector2(0.5f, 0.5f);
            inputRect.offsetMin = new Vector2(0, 8);
            inputRect.offsetMax = new Vector2(0, -8);
            searchInput.onValueChanged.AddListener(v => {
                currentQuery = v ?? "";
                RenderList();
            });
            StartCoroutine(FocusSearchNextFrame());

            var (scrollObj, scrollRect) = UIHelpers.Create("Scroll", popup.transform);
            scrollRect.anchorMin = new Vector2(0, 0);
            scrollRect.anchorMax = new Vector2(1, 1);
            scrollRect.pivot = new Vector2(0.5f, 0.5f);
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = new Vector2(0, -headerHeight);  // inset from top by header height

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
            vlg.padding = new RectOffset(4, 4, 4, 4);

            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollObj.AddComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;

            rowsContainer = content;
        }

        System.Collections.IEnumerator FocusSearchNextFrame() {
            yield return null;
            if (searchInput != null) {
                searchInput.Select();
                searchInput.ActivateInputField();
            }
        }

        void Update() {
            if (closed) return;
            if (Input.GetKeyDown(KeyCode.Escape)) {
                closed = true;
                var overlay = transform.parent != null ? transform.parent.gameObject : gameObject;
                Destroy(overlay);
            }
        }

        void RenderList() {
            if (rowsContainer == null) return;

            for (int i = rowsContainer.transform.childCount - 1; i >= 0; i--) {
                Destroy(rowsContainer.transform.GetChild(i).gameObject);
            }

            var matches = FilterAndRank(currentQuery);
            if (matches.Count == 0) {
                AddInfoLabel(string.IsNullOrEmpty(currentQuery)
                    ? "(no abilities available)"
                    : $"No matches for \"{currentQuery}\"");
                return;
            }

            foreach (var entry in matches) AddRow(entry);
        }

        // Tier-1 = name starts with query, Tier-2 = contains query. Within a tier,
        // shorter names first, then alphabetical. Empty query returns all entries
        // in their original order (SpellDropdownProvider already sorts them).
        List<SpellDropdownProvider.SpellEntry> FilterAndRank(string query) {
            if (string.IsNullOrEmpty(query)) return entries;

            string needle = query.Trim().ToLowerInvariant();
            if (needle.Length == 0) return entries;

            var all_matches = new List<SpellDropdownProvider.SpellEntry>();
            foreach (var e in entries) {
                if (string.IsNullOrEmpty(e.Name)) continue;
                if (e.Name.ToLowerInvariant().Contains(needle))
                    all_matches.Add(e);
            }

            all_matches.Sort((a, b) => {
                int ap = a.Name.ToLowerInvariant().StartsWith(needle) ? 0 : 1;
                int bp = b.Name.ToLowerInvariant().StartsWith(needle) ? 0 : 1;
                if (ap != bp) return ap - bp;
                if (a.Name.Length != b.Name.Length) return a.Name.Length - b.Name.Length;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            return all_matches;
        }

        void AddInfoLabel(string text) {
            var (info, _) = UIHelpers.Create("Info", rowsContainer.transform);
            info.AddComponent<LayoutElement>().preferredHeight = 28;
            var label = UIHelpers.AddLabel(info, text, 13f, TextAlignmentOptions.Midline,
                new Color(0.55f, 0.55f, 0.55f));
            label.margin = new Vector4(8, 0, 8, 0);
        }

        void AddRow(SpellDropdownProvider.SpellEntry entry) {
            var (row, _) = UIHelpers.Create("Row_" + (entry.Guid ?? ""), rowsContainer.transform);
            row.AddComponent<LayoutElement>().preferredHeight = 32;
            UIHelpers.AddBackground(row, new Color(0.2f, 0.2f, 0.2f, 1f));

            // Icon on the left (28x28 inside the 32px row)
            if (entry.Icon != null) {
                var (iconGO, iconRect) = UIHelpers.Create("Icon", row.transform);
                iconRect.SetAnchor(0, 0, 0.5, 0.5);
                iconRect.pivot = new Vector2(0, 0.5f);
                iconRect.anchoredPosition = new Vector2(4, 0);
                iconRect.sizeDelta = new Vector2(28, 28);
                var img = iconGO.AddComponent<Image>();
                img.sprite = entry.Icon;
                img.raycastTarget = false;
            }

            var label = UIHelpers.AddLabel(row, entry.Name, 14f, TextAlignmentOptions.MidlineLeft);
            label.margin = new Vector4(entry.Icon != null ? 40 : 8, 0, 4, 0);

            var captured = entry;
            row.AddComponent<Button>().onClick.AddListener(() => onSelected?.Invoke(captured));
        }
    }
}
