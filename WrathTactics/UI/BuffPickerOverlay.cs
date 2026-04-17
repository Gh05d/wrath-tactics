using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Kingmaker;
using WrathTactics.Engine;
using WrathTactics.Logging;
using WrathTactics.Models;
using WrathTactics.Persistence;

namespace WrathTactics.UI {
    /// <summary>
    /// Search-first overlay for selecting a BlueprintBuff GUID.
    /// Replaces the generic PopupSelector for HasBuff/MissingBuff conditions to
    /// avoid instantiating ~3000 rows up front.
    /// </summary>
    public class BuffPickerOverlay : MonoBehaviour {
        const int MaxFilterResults = 50;
        const int MaxRecents = 5;

        ConditionSubject subject;
        Action<string> onSelected;
        TMP_InputField searchInput;
        GameObject rowsContainer;
        string currentQuery = "";
        bool closed;

        public static GameObject Open(string currentGuid, ConditionSubject subject,
            Action<string> onSelected) {
            var canvas = Game.Instance.UI.Canvas.transform;

            var (overlay, overlayRect) = UIHelpers.Create("BuffPickerOverlay", canvas);
            overlayRect.FillParent();
            UIHelpers.AddBackground(overlay, new Color(0, 0, 0, 0.4f));
            overlay.AddComponent<Button>().onClick.AddListener(() => Destroy(overlay));

            var (popup, popupRect) = UIHelpers.Create("Popup", overlay.transform);
            UIHelpers.AddBackground(popup, new Color(0.12f, 0.12f, 0.12f, 0.99f));
            popupRect.anchorMin = new Vector2(0.5f, 0.5f);
            popupRect.anchorMax = new Vector2(0.5f, 0.5f);
            popupRect.pivot = new Vector2(0.5f, 0.5f);
            popupRect.anchoredPosition = Vector2.zero;
            popupRect.sizeDelta = new Vector2(420f, 500f);

            // Prevent clicks on the popup from bubbling to the overlay (which would close it).
            // Bind the Button's targetGraphic to the background Image that AddBackground attached.
            var swallow = popup.AddComponent<Button>();
            swallow.targetGraphic = popup.GetComponent<Image>();

            var controller = popup.AddComponent<BuffPickerOverlay>();
            controller.subject = subject;
            controller.onSelected = guid => {
                if (controller.closed) return;
                controller.closed = true;
                RecordRecent(guid);
                onSelected?.Invoke(guid);
                Destroy(overlay);
            };
            controller.BuildUI(popup);
            controller.RenderList(); // initial render with empty query

            return overlay;
        }

        void BuildUI(GameObject popup) {
            const float headerHeight = 40f;

            // Header: pinned to the top, 40px tall. Pivot at top-center so
            // anchoredPosition (0,0) puts its top edge flush with the popup's top edge.
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
            inputRect.SetAnchor(0.02f, 0.98f, 0.1f, 0.9f);
            inputRect.sizeDelta = Vector2.zero;
            searchInput.onValueChanged.AddListener(v => {
                currentQuery = v ?? "";
                RenderList();
            });
            // Autofocus on next frame (Unity quirk — calling now can no-op before the
            // EventSystem picks up the new object).
            StartCoroutine(FocusSearchNextFrame());

            // Body: scroll view fills the area BELOW the header. Anchor stretched on
            // both axes, then inset `headerHeight` off the top via offsetMax.y.
            var (scrollObj, scrollRect) = UIHelpers.Create("Scroll", popup.transform);
            scrollRect.anchorMin = new Vector2(0, 0);
            scrollRect.anchorMax = new Vector2(1, 1);
            scrollRect.pivot = new Vector2(0.5f, 0.5f);
            scrollRect.offsetMin = Vector2.zero;
            scrollRect.offsetMax = new Vector2(0, -headerHeight);

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

            // Clear existing rows
            for (int i = rowsContainer.transform.childCount - 1; i >= 0; i--) {
                Destroy(rowsContainer.transform.GetChild(i).gameObject);
            }

            if (string.IsNullOrEmpty(currentQuery)) {
                RenderDefaultsLayout();
            } else {
                RenderFilteredLayout(currentQuery);
            }
        }

        void RenderDefaultsLayout() {
            var recents = GetRecentEntries();
            var defaults = GetDefaultEntries();

            // Dedupe: if a buff is in recents, don't repeat it in defaults.
            var recentGuids = new HashSet<string>(recents.Select(e => e.Guid));
            var dedupedDefaults = defaults.Where(e => !recentGuids.Contains(e.Guid)).ToList();

            if (recents.Count > 0) {
                AddSectionHeader("★ Recents");
                foreach (var e in recents) AddRow(e);
            }

            if (dedupedDefaults.Count > 0) {
                string label = CommonBuffRegistry.IsEnemySubject(subject) ? "Common Enemy Buffs" : "Common Ally Buffs";
                AddSectionHeader(label);
                foreach (var e in dedupedDefaults) AddRow(e);
            }

            if (recents.Count == 0 && dedupedDefaults.Count == 0) {
                AddInfoLabel("(no suggestions available — start typing to search)");
            }
        }

        void RenderFilteredLayout(string query) {
            var all = BuffBlueprintProvider.GetBuffs();
            string needle = query.ToLowerInvariant();
            var matches = new List<BuffBlueprintProvider.BuffEntry>();
            foreach (var entry in all) {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                if (entry.Name.ToLowerInvariant().Contains(needle)) {
                    matches.Add(entry);
                    if (matches.Count >= MaxFilterResults) break;
                }
            }

            if (matches.Count == 0) {
                AddInfoLabel($"No matches for \"{query}\"");
                return;
            }

            foreach (var entry in matches) AddRow(entry);

            if (matches.Count == MaxFilterResults) {
                AddInfoLabel($"(showing first {MaxFilterResults} matches — refine your search for more)");
            }
        }

        List<BuffBlueprintProvider.BuffEntry> GetRecentEntries() {
            var cfg = ConfigManager.Current;
            var all = BuffBlueprintProvider.GetBuffs();
            var result = new List<BuffBlueprintProvider.BuffEntry>();
            foreach (var guid in cfg.RecentBuffGuids) {
                var entry = all.FirstOrDefault(b => b.Guid == guid);
                if (!string.IsNullOrEmpty(entry.Guid)) result.Add(entry);
            }
            return result;
        }

        List<BuffBlueprintProvider.BuffEntry> GetDefaultEntries() {
            var all = BuffBlueprintProvider.GetBuffs();
            var guids = CommonBuffRegistry.GetDefaultGuids(subject);
            var result = new List<BuffBlueprintProvider.BuffEntry>();
            foreach (var guid in guids) {
                var entry = all.FirstOrDefault(b => b.Guid == guid);
                if (!string.IsNullOrEmpty(entry.Guid)) result.Add(entry);
            }
            return result;
        }

        void AddSectionHeader(string text) {
            var (hdr, _) = UIHelpers.Create("Header_" + text, rowsContainer.transform);
            hdr.AddComponent<LayoutElement>().preferredHeight = 24;
            UIHelpers.AddBackground(hdr, new Color(0.08f, 0.08f, 0.08f, 1f));
            var label = UIHelpers.AddLabel(hdr, text, 13f, TextAlignmentOptions.MidlineLeft,
                new Color(0.7f, 0.7f, 0.7f));
            label.margin = new Vector4(6, 0, 4, 0);
        }

        void AddInfoLabel(string text) {
            var (info, _) = UIHelpers.Create("Info", rowsContainer.transform);
            info.AddComponent<LayoutElement>().preferredHeight = 28;
            var label = UIHelpers.AddLabel(info, text, 13f, TextAlignmentOptions.Midline,
                new Color(0.55f, 0.55f, 0.55f));
            label.margin = new Vector4(8, 0, 8, 0);
        }

        void AddRow(BuffBlueprintProvider.BuffEntry entry) {
            var (row, _) = UIHelpers.Create("Row_" + entry.Guid, rowsContainer.transform);
            row.AddComponent<LayoutElement>().preferredHeight = 32;
            UIHelpers.AddBackground(row, new Color(0.2f, 0.2f, 0.2f, 1f));
            var label = UIHelpers.AddLabel(row, entry.Name, 14f, TextAlignmentOptions.MidlineLeft);
            label.margin = new Vector4(8, 0, 4, 0);
            var guid = entry.Guid;
            row.AddComponent<Button>().onClick.AddListener(() => onSelected?.Invoke(guid));
        }


        static void RecordRecent(string guid) {
            if (string.IsNullOrEmpty(guid)) return;
            var cfg = ConfigManager.Current;
            cfg.RecentBuffGuids.Remove(guid);
            cfg.RecentBuffGuids.Insert(0, guid);
            while (cfg.RecentBuffGuids.Count > MaxRecents) {
                cfg.RecentBuffGuids.RemoveAt(cfg.RecentBuffGuids.Count - 1);
            }
            ConfigManager.Save();
        }
    }
}
