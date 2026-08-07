using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.PubSubSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Localization;
using WrathTactics.Logging;
using WrathTactics.Models;
using WrathTactics.Persistence;
using Object = UnityEngine.Object;

namespace WrathTactics.UI {
    public class TacticsPanel : MonoBehaviour, IPartyCombatHandler {
        static TacticsPanel instance;
        GameObject panelRoot;
        GameObject hudButton;
        bool isVisible;
        float panelBuiltAtFontScale; // tracks the FontScale used when panelRoot was last built
        string selectedUnitId; // null = Global, "presets" = Presets
        string lastNonPresetUnitId; // last selected tab that wasn't "presets"
        Transform ruleListContent; // parent for rule cards
        TextMeshProUGUI toggleLabel;
        Transform tabBarTransform; // reference to rebuild tabs

        // Filter state
        string currentRuleFilter = "";
        TMP_InputField ruleFilterInput;
        Button ruleFilterClearButton;
        GameObject ruleFilterEmptyLabel;  // sibling of rule scroll, shown when filter hides everything
        PresetPanel currentPresetPanel;    // tracks the active PresetPanel when presets tab is open

        // Result of the last pack action, rendered in the pack row. Mirrors
        // PresetPanel.lastIOStatus: the row is rebuilt constantly, so the text must
        // live on the panel, not on the label.
        string lastPackStatus;
        Color lastPackStatusColor = Color.gray;

        public static TacticsPanel Instance => instance;

        // Lets external UI (portrait badges) refresh the open panel after
        // flipping TacticsEnabled, so the header toggle label never goes stale.
        // Guards on isVisible (the panel's real visibility flag — the instance
        // itself sits on the always-active DontDestroyOnLoad controller).
        public static void NotifyExternalConfigChange() {
            if (instance == null || !instance.isVisible) return;
            instance.RefreshRuleList();
        }

        public static void Install() {
            if (instance != null) return;

            var go = new GameObject("WrathTacticsController");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<TacticsPanel>();
            EventBus.Subscribe(instance);
            Log.UI.Info("TacticsPanel installed");
        }

        public static void Uninstall() {
            if (instance != null) {
                EventBus.Unsubscribe(instance);
                if (instance.panelRoot != null) Destroy(instance.panelRoot);
                if (instance.hudButton != null) Destroy(instance.hudButton);
                Destroy(instance.gameObject);
                instance = null;
                Log.UI.Info("TacticsPanel uninstalled");
            }
        }

        public void Toggle() {
            // Refresh once per open. The picked-up value drives every AddLabel /
            // CreateTMPInputField fontSize during this session — settings changes
            // made while the panel is visible take effect on the next Ctrl+T cycle.
            if (panelRoot == null || !isVisible) {
                UIHelpers.RefreshFontScale();
                // Tab/rule chrome is rebuilt on every open; the static title bar etc.
                // built in CreatePanel sticks at its build-time scale. Tear down the
                // panel root if the user changed the scale slider between sessions so
                // the next CreatePanel picks up the new value uniformly.
                if (panelRoot != null && !Mathf.Approximately(panelBuiltAtFontScale, UIHelpers.FontScale)) {
                    Destroy(panelRoot);
                    panelRoot = null;
                }
            }
            if (panelRoot == null) {
                CreatePanel();
                panelBuiltAtFontScale = UIHelpers.FontScale;
            }
            isVisible = !isVisible;
            panelRoot.SetActive(isVisible);
            if (isVisible) {
                RebuildTabs();
                RefreshRuleList();
            }
        }

        void CreatePanel() {
            var canvas = Game.Instance.UI.Canvas.transform;

            // Outer backdrop: opaque fullscreen black so the game world does not show
            // through the book's letterbox margin.
            var (root, rootRect) = UIHelpers.Create("WrathTacticsPanel", canvas);
            panelRoot = root;
            rootRect.SetAnchor(0, 1, 0, 1);
            rootRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(root, new Color(0, 0, 0, 1f));

            // Inner book panel: open-book illustration centered. AspectRatioFitter mode
            // FitInParent enforces native 2:1 aspect — letterbox top/bottom on 16:9.
            var (book, bookRect) = UIHelpers.Create("BookPanel", root.transform);
            var fitter = book.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 2.0f;
            if (ThemeProvider.PanelBackground != null) {
                ThemeProvider.ApplyPanel(book);
            } else {
                UIHelpers.AddBackground(book, new Color(0.15f, 0.12f, 0.08f, 0.98f));
            }

            // Inner content area — sits ON the book pages, leaves visible book frame
            // around it (decorative edges + spine remain visible). All widgets parent
            // under this rect, not under `book`, so the book art frames the menu.
            var (bookContent, contentRect) = UIHelpers.Create("BookContent", book.transform);
            contentRect.SetAnchor(0.06, 0.94, 0.10, 0.93);
            contentRect.sizeDelta = Vector2.zero;

            // Title bar
            var (titleBar, titleRect) = UIHelpers.Create("TitleBar", bookContent.transform);
            titleRect.SetAnchor(0, 1, 0.92, 1);
            titleRect.sizeDelta = Vector2.zero;
            if (ThemeProvider.TitleBarBackground != null) {
                var img = titleBar.AddComponent<Image>();
                img.sprite = ThemeProvider.TitleBarBackground;
                img.type = Image.Type.Sliced;
                img.color = Color.white;
                img.raycastTarget = true;
            } else {
                UIHelpers.AddBackground(titleBar, new Color(0.2f, 0.15f, 0.1f, 1f));
            }
            var titleLabel = UIHelpers.AddLabel(titleBar, "panel.title".i18n(), 26f, TextAlignmentOptions.Midline);
            titleLabel.outlineWidth = 0.25f;
            titleLabel.outlineColor = new Color32(0, 0, 0, 255);

            // Close button
            var (closeBtn, closeRect) = UIHelpers.Create("CloseButton", titleBar.transform);
            closeRect.SetAnchor(0.95, 1, 0, 1);
            closeRect.sizeDelta = Vector2.zero;
            if (ThemeProvider.CloseButtonNormal != null) {
                ThemeProvider.ApplyCloseButton(closeBtn);
            } else {
                UIHelpers.AddBackground(closeBtn, new Color(0.6f, 0.2f, 0.2f, 1f));
                closeBtn.AddComponent<Button>();
            }
            UIHelpers.AddLabel(closeBtn, "X", 22f, TextAlignmentOptions.Midline);
            closeBtn.GetComponent<Button>().onClick.AddListener(Toggle);

            // Tab bar
            var (tabBar, tabRect) = UIHelpers.Create("TabBar", bookContent.transform);
            tabRect.SetAnchor(0, 1, 0.84, 0.91);
            tabRect.sizeDelta = Vector2.zero;
            tabBarTransform = tabBar.transform;

            var hlg = tabBar.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            RebuildTabs();

            // Toggle + Add rule row
            CreateControlRow(bookContent.transform);

            // Filter strip (sticky — stays above the scroll area regardless of tab)
            CreateFilterStrip(bookContent.transform);

            // Scrollable rule list
            CreateRuleList(bookContent.transform);

            // Empty-state label for the rule list (hidden by default, driven by ApplyFilter)
            CreateRuleFilterEmptyLabel(bookContent.transform);

            // Retrofit hover-feedback on every flat-color Button in the panel tree.
            // Themed buttons (SpriteSwap) are left untouched because their hover
            // sprites are already wired.
            UIHelpers.EnsureAllHoverable(panelRoot);

            panelRoot.SetActive(false);
            Log.UI.Info("Panel created");
        }

        void RebuildTabs() {
            if (tabBarTransform == null) return;

            // Clear existing tabs
            for (int i = tabBarTransform.childCount - 1; i >= 0; i--)
                Destroy(tabBarTransform.GetChild(i).gameObject);

            // Global tab
            AddTab(tabBarTransform.gameObject, "tab.global".i18n(), null, () => SelectTab(null));

            // Party member + pet tabs
            if (Game.Instance?.Player?.PartyAndPets != null) {
                foreach (var unit in Game.Instance.Player.PartyAndPets) {
                    if (!unit.IsInGame) continue;
                    var uid = unit.UniqueId;
                    AddTab(tabBarTransform.gameObject, unit.CharacterName, uid, () => SelectTab(uid));
                }
            }

            // Presets tab
            AddTab(tabBarTransform.gameObject, "tab.presets".i18n(), "presets", () => SelectTab("presets"));
        }

        static readonly Color TabNormal = new Color(0.25f, 0.2f, 0.15f, 1f);
        static readonly Color TabSelected = new Color(0.4f, 0.3f, 0.15f, 1f);

        void AddTab(GameObject parent, string label, string tabId, UnityEngine.Events.UnityAction onClick) {
            var (btn, _) = UIHelpers.Create($"Tab_{label}", parent.transform);
            bool isSelected = (tabId == null && selectedUnitId == null)
                || (tabId != null && tabId == selectedUnitId);

            // Order matters: Button must exist BEFORE ApplyTabHeader so the theme code
            // can wire its SpriteSwap states for hover/press visibility.
            var button = btn.AddComponent<Button>();
            button.onClick.AddListener(onClick);

            var themed = isSelected ? ThemeProvider.TabHeaderActive : ThemeProvider.TabHeaderInactive;
            if (themed != null) {
                ThemeProvider.ApplyTabHeader(btn, isSelected);
            } else {
                UIHelpers.AddBackground(btn, isSelected ? TabSelected : TabNormal);
            }

            UIHelpers.AddLabel(btn, label, 16f, TextAlignmentOptions.Midline);
        }

        void SelectTab(string unitId) {
            if (selectedUnitId != "presets")
                lastNonPresetUnitId = selectedUnitId;
            selectedUnitId = unitId;

            // Don't carry one character's pack message over to the next tab.
            lastPackStatus = null;

            // Reset the filter on tab switch (fires onValueChanged -> sets currentRuleFilter = "").
            if (ruleFilterInput != null)
                ruleFilterInput.text = "";

            RebuildTabs();
            RefreshRuleList();
        }

        void CreateControlRow(Transform parent) {
            var (row, rowRect) = UIHelpers.Create("ControlRow", parent);
            rowRect.SetAnchor(0.01, 0.99, 0.77, 0.83);
            rowRect.sizeDelta = Vector2.zero;

            // Toggle
            var (toggleBtn, toggleRect) = UIHelpers.Create("ToggleBtn", row.transform);
            toggleRect.SetAnchor(0, 0.5, 0, 1);
            toggleRect.sizeDelta = Vector2.zero;
            toggleLabel = UIHelpers.AddLabel(toggleBtn, "toggle.global_rules".i18n(), 18f,
                TextAlignmentOptions.MidlineLeft, Color.white);
            toggleBtn.AddComponent<Button>().onClick.AddListener(ToggleTactics);

            // "+ New Rule" button
            var (addBtn, addRect) = UIHelpers.Create("AddRuleBtn", row.transform);
            addRect.SetAnchor(0.55, 0.76, 0, 1);
            addRect.sizeDelta = Vector2.zero;
            if (ThemeProvider.ActionButtonNormal != null) {
                ThemeProvider.ApplyActionButton(addBtn);
            } else {
                UIHelpers.AddBackground(addBtn, new Color(0.2f, 0.4f, 0.2f, 1f));
                addBtn.AddComponent<Button>();
            }
            UIHelpers.AddLabel(addBtn, "button.new_rule".i18n(), 18f, TextAlignmentOptions.Midline);
            addBtn.GetComponent<Button>().onClick.AddListener(AddNewRule);

            // "+ From Preset" button
            var (fromPresetBtn, fromPresetRect) = UIHelpers.Create("FromPresetBtn", row.transform);
            fromPresetRect.SetAnchor(0.77, 1, 0, 1);
            fromPresetRect.sizeDelta = Vector2.zero;
            if (ThemeProvider.ActionButtonNormal != null) {
                ThemeProvider.ApplyActionButton(fromPresetBtn);
            } else {
                UIHelpers.AddBackground(fromPresetBtn, new Color(0.2f, 0.35f, 0.5f, 1f));
                fromPresetBtn.AddComponent<Button>();
            }
            UIHelpers.AddLabel(fromPresetBtn, "button.from_preset".i18n() + " \u25be", 16f, TextAlignmentOptions.Midline);
            fromPresetBtn.GetComponent<Button>().onClick.AddListener(AddFromPreset);
        }

        void CreateFilterStrip(Transform parent) {
            var (strip, stripRect) = UIHelpers.Create("FilterStrip", parent);
            stripRect.SetAnchor(0.01, 0.99, 0.72, 0.76);
            stripRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(strip, new Color(0.14f, 0.14f, 0.14f, 1f));

            ruleFilterInput = UIHelpers.CreateTMPInputField(strip, "FilterInput",
                0.02, 0.85, "", 15f,
                placeholderText: "filter.rules.placeholder".i18n());
            var inputRect = ruleFilterInput.GetComponent<RectTransform>();
            inputRect.SetAnchor(0.02f, 0.85f, 0.1f, 0.9f);
            inputRect.sizeDelta = Vector2.zero;
            ruleFilterInput.onValueChanged.AddListener(v => {
                currentRuleFilter = v ?? "";
                UpdateFilterClearButton();
                ApplyFilter();
            });

            // Clear (×) button
            var (clearBtn, clearRect) = UIHelpers.Create("FilterClear", strip.transform);
            clearRect.SetAnchor(0.87f, 0.98f, 0.15f, 0.85f);
            clearRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(clearBtn, new Color(0.3f, 0.3f, 0.3f, 1f));
            UIHelpers.AddLabel(clearBtn, "✕", 16f, TextAlignmentOptions.Midline);
            ruleFilterClearButton = clearBtn.AddComponent<Button>();
            ruleFilterClearButton.onClick.AddListener(() => {
                ruleFilterInput.text = "";  // triggers onValueChanged -> ApplyFilter
            });
            // Hide entirely when the filter is empty — a disabled-but-visible grey block
            // looks like a UI leftover. Visibility is driven by UpdateFilterClearButton.
            clearBtn.SetActive(false);
        }

        void UpdateFilterClearButton() {
            if (ruleFilterClearButton == null) return;
            bool hasFilter = !string.IsNullOrEmpty(currentRuleFilter);
            ruleFilterClearButton.gameObject.SetActive(hasFilter);
            ruleFilterClearButton.interactable = hasFilter;
        }

        void CreateRuleFilterEmptyLabel(Transform parent) {
            var (obj, rect) = UIHelpers.Create("RuleFilterEmpty", parent);
            // Same anchor as the rule scroll so the label overlays its center
            rect.SetAnchor(0.01, 0.99, 0.02, 0.71);
            rect.sizeDelta = Vector2.zero;
            // Sits directly on the book-page art like the hints — needs the outline
            // pattern for contrast (see UIHelpers.AddHintCard).
            var emptyLabel = UIHelpers.AddLabel(obj, "filter.no_matching_rules".i18n(), 16f,
                TextAlignmentOptions.Midline, new Color(0.75f, 0.75f, 0.75f));
            emptyLabel.outlineWidth = 0.25f;
            emptyLabel.outlineColor = new Color32(0, 0, 0, 255);
            obj.SetActive(false);
            ruleFilterEmptyLabel = obj;
        }

        void ApplyFilter() {
            if (ruleListContent == null) return;

            if (selectedUnitId == "presets") {
                // Hide the char/global empty label — it's not ours on this tab.
                if (ruleFilterEmptyLabel != null) ruleFilterEmptyLabel.SetActive(false);
                // Unity's "==" operator returns true for destroyed MonoBehaviours, so
                // this null-check catches both a never-set reference and a destroyed one.
                if (currentPresetPanel != null)
                    currentPresetPanel.ApplyFilter(currentRuleFilter);
                return;
            }

            int visible = 0;
            int total = 0;
            for (int i = 0; i < ruleListContent.childCount; i++) {
                var child = ruleListContent.GetChild(i).gameObject;
                var widget = child.GetComponent<RuleEditorWidget>();
                if (widget == null) continue;  // safety — only rule cards are counted
                total++;
                string name = EffectiveDisplayName(widget.Rule);
                bool match = UIHelpers.StringMatchesFilter(name, currentRuleFilter);
                child.SetActive(match);
                if (match) visible++;
            }

            bool filterActive = !string.IsNullOrWhiteSpace(currentRuleFilter);
            if (ruleFilterEmptyLabel != null)
                ruleFilterEmptyLabel.SetActive(filterActive && total > 0 && visible == 0);
        }

        void CreateRuleList(Transform parent) {
            // ScrollRect container — no background; each rule card carries its own
            // parchment sheet (see RuleEditorWidget.BuildUI).
            var (scrollObj, scrollRect) = UIHelpers.Create("RuleScroll", parent);
            scrollRect.SetAnchor(0.01, 0.99, 0.02, 0.71);
            scrollRect.sizeDelta = Vector2.zero;

            // Viewport with RectMask2D instead of Mask.
            // Reserve a gutter on the right for the permanent scrollbar (12 px track
            // + 4 px spacing = 16 px). ScrollbarVisibility.Permanent does NOT auto-
            // shrink the viewport the way AutoHideAndExpandViewport does — without
            // this inset, rule-card content near the right edge renders behind the
            // scrollbar track.
            var (viewport, viewportRect) = UIHelpers.Create("Viewport", scrollObj.transform);
            viewportRect.FillParent();
            viewportRect.offsetMax = new Vector2(-16, 0);
            viewport.AddComponent<RectMask2D>();

            // Content container with vertical layout
            var (content, contentRect) = UIHelpers.Create("Content", viewport.transform);
            contentRect.SetAnchor(0, 1, 1, 1);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(0, 0);

            var vlg = content.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.padding = new RectOffset(4, 4, 4, 4);

            var csf = content.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Vertical scrollbar track on the right edge
            var (scrollbarObj, scrollbarRect) = UIHelpers.Create("Scrollbar", scrollObj.transform);
            scrollbarRect.SetAnchor(1, 1, 0, 1);
            scrollbarRect.pivot = new Vector2(1, 0.5f);
            scrollbarRect.sizeDelta = new Vector2(12, 0);
            if (ThemeProvider.ScrollbarTrack != null) {
                var trackImg = scrollbarObj.AddComponent<Image>();
                trackImg.sprite = ThemeProvider.ScrollbarTrack;
                trackImg.type = Image.Type.Sliced;
                trackImg.color = Color.white;
            } else {
                UIHelpers.AddBackground(scrollbarObj, new Color(0.15f, 0.15f, 0.15f, 0.85f));
            }

            var (handleObj, handleRect) = UIHelpers.Create("Handle", scrollbarObj.transform);
            handleRect.sizeDelta = Vector2.zero;
            if (ThemeProvider.ScrollbarHandle != null) {
                var handleImg = handleObj.AddComponent<Image>();
                handleImg.sprite = ThemeProvider.ScrollbarHandle;
                handleImg.type = Image.Type.Sliced;
                handleImg.color = Color.white;
            } else {
                UIHelpers.AddBackground(handleObj, new Color(0.7f, 0.7f, 0.7f, 1.0f));
            }

            var scrollbar = scrollbarObj.AddComponent<Scrollbar>();
            scrollbar.handleRect = handleRect;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.targetGraphic = handleObj.GetComponent<Image>();

            // Wire ScrollRect
            var scroll = scrollObj.AddComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
            scroll.verticalScrollbarSpacing = 4f;

            ruleListContent = content.transform;
        }

        void RefreshRuleList() {
            if (ruleListContent == null) return;

            // Clear existing cards (this destroys any prior PresetPanel too). Detach
            // first so the same-frame ApplyFilter pass and VLG layout only see live
            // cards — Destroy() lands at end of frame.
            while (ruleListContent.childCount > 0) {
                var child = ruleListContent.GetChild(0);
                child.SetParent(null, false);
                Destroy(child.gameObject);
            }
            currentPresetPanel = null;

            if (selectedUnitId == "presets") {
                var (presetObj, _) = UIHelpers.Create("PresetPanel", ruleListContent);
                var presetPanel = presetObj.AddComponent<PresetPanel>();
                presetPanel.Init(lastNonPresetUnitId, ruleListContent, () => RefreshRuleList());
                currentPresetPanel = presetPanel;
                UpdateToggleLabel();
                ApplyFilter();
                return;
            }

            var config = ConfigManager.Current;
            var rules = selectedUnitId == null
                ? config.GlobalRules
                : config.GetRulesForCharacter(selectedUnitId);

            UpdateToggleLabel();

            // Global-tab explainer: global rules are evaluated before every character's
            // own rules each tick, and a firing global rule (or its still-running command)
            // skips the character list entirely (TacticsEvaluator.EvaluateUnit /
            // ActiveRuleTracker.Resolve). Users who park a catch-all Attack rule here
            // starve all per-character rules and report "my rules never fire" — surface
            // the semantics where the rules are edited. Plain label card without
            // RuleEditorWidget — ApplyFilter ignores it.
            if (selectedUnitId == null) {
                UIHelpers.AddHintCard(ruleListContent, "global.priority_hint".i18n());
                AddHudButtonToggleRow();
            } else {
                // Char-tab counterpart: show how many enabled global rules run ahead
                // of this list — the "my rules never fire" symptom is debugged here.
                int enabledGlobals = 0;
                foreach (var r in config.GlobalRules) if (r.Enabled) enabledGlobals++;
                if (enabledGlobals > 0)
                    UIHelpers.AddHintCard(ruleListContent,
                        Strings.Format("global.preempt_hint", enabledGlobals), 24f);
            }

            AddPackRow(rules);

            for (int i = 0; i < rules.Count; i++) {
                var (card, _) = UIHelpers.Create($"Rule_{i}", ruleListContent);
                var widget = card.AddComponent<RuleEditorWidget>();
                var capturedRules = rules;
                widget.Init(rules[i], i, capturedRules, () => RefreshRuleList(), selectedUnitId);
            }

            ApplyFilter();
        }

        // Global-tab setting row: shows/hides the HUD button (controller players have no
        // cursor to click it and want it gone; Ctrl+T keeps working). Lives in the rule
        // list like the hint cards — no RuleEditorWidget, so ApplyFilter ignores it.
        void AddHudButtonToggleRow() {
            var (row, _) = UIHelpers.Create("HudButtonToggle", ruleListContent);
            row.AddComponent<LayoutElement>().preferredHeight = 32f * UIHelpers.FontScale;

            TextMeshProUGUI label = null;
            var btn = UIHelpers.MakeButton(row.transform, "HudButtonToggleBtn", HudToggleLabel(), 14f,
                new Color(0.25f, 0.2f, 0.15f, 1f), () => {
                    var settings = ModSettingsManager.Current;
                    settings.ShowHudButton = !settings.ShowHudButton;
                    bool saved = ModSettingsManager.Save();
                    // Re-enabling should give instant feedback: push the floating-fallback
                    // retry timer past its threshold so Update() recreates the button on
                    // the next frame instead of after the 5 s BubbleBuffs grace period.
                    if (settings.ShowHudButton) hudButtonRetrySeconds = 6f;
                    // Update the label in place — a full RefreshRuleList would reset the
                    // rule list scroll position just to repaint one string.
                    if (label != null)
                        label.text = saved ? HudToggleLabel() : "hud_button.save_failed".i18n();
                });
            btn.FillParent();
            label = btn.GetComponentInChildren<TextMeshProUGUI>();
            UIHelpers.EnsureAllHoverable(row);
        }

        // Applied-packs strip: one chip per pack present in this list plus the apply button.
        // Lives in the rule list like the hint cards — no RuleEditorWidget, so ApplyFilter
        // ignores it. Chips are per-pack, so a companion can carry any number of packs.
        void AddPackRow(List<TacticsRule> rules) {
            var (row, _) = UIHelpers.Create("PackRow", ruleListContent);
            row.AddComponent<LayoutElement>().preferredHeight = 32f * UIHelpers.FontScale;

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.padding = new RectOffset(4, 4, 2, 2);
            hlg.childAlignment = TextAnchor.MiddleLeft;

            var (labelObj, _l) = UIHelpers.Create("PackRowLabel", row.transform);
            var labelLE = labelObj.AddComponent<LayoutElement>();
            labelLE.preferredWidth = 70;
            labelLE.flexibleWidth = 0;
            UIHelpers.AddLabel(labelObj, "pack.row_label".i18n(), 14f,
                TextAlignmentOptions.MidlineLeft, Color.white);

            foreach (var packId in Engine.PackRegistry.AppliedPackIds(rules)) {
                var pack = Engine.PackRegistry.Get(packId);
                if (pack == null) continue;  // pack deleted — rules keep working, no chip
                var captured = pack;
                var (chip, _c) = UIHelpers.Create($"PackChip_{pack.Id}", row.transform);
                var chipLE = chip.AddComponent<LayoutElement>();
                chipLE.preferredWidth = 130;
                chipLE.flexibleWidth = 0;
                UIHelpers.AddBackground(chip, PackPalette.ColorAt(pack.ColorIndex));
                UIHelpers.AddLabel(chip, pack.Name + "  ×", 13f, TextAlignmentOptions.Midline);
                chip.AddComponent<Button>().onClick.AddListener(() => RemovePackFromList(captured));
            }

            var (applyBtn, _a) = UIHelpers.Create("ApplyPackBtn", row.transform);
            var applyLE = applyBtn.AddComponent<LayoutElement>();
            applyLE.preferredWidth = 120;
            applyLE.flexibleWidth = 0;
            UIHelpers.AddBackground(applyBtn, new Color(0.2f, 0.4f, 0.45f, 1f));
            UIHelpers.AddLabel(applyBtn, "pack.button.apply".i18n(), 14f, TextAlignmentOptions.Midline);
            applyBtn.AddComponent<Button>().onClick.AddListener(ShowPackPicker);

            var (saveBtn, _s) = UIHelpers.Create("SaveListAsPackBtn", row.transform);
            var saveLE = saveBtn.AddComponent<LayoutElement>();
            saveLE.preferredWidth = 150;
            saveLE.flexibleWidth = 0;
            UIHelpers.AddBackground(saveBtn, new Color(0.25f, 0.45f, 0.3f, 1f));
            UIHelpers.AddLabel(saveBtn, "pack.button.save_list".i18n(), 14f, TextAlignmentOptions.Midline);
            saveBtn.AddComponent<Button>().onClick.AddListener(SaveListAsPack);

            // Result of the last pack action — the character tab has no status line of its
            // own, and a silent "nothing happened" is indistinguishable from a broken button.
            if (!string.IsNullOrEmpty(lastPackStatus)) {
                var (statusObj, _st) = UIHelpers.Create("PackStatus", row.transform);
                var statusLE = statusObj.AddComponent<LayoutElement>();
                statusLE.preferredWidth = 240;
                statusLE.flexibleWidth = 1;
                UIHelpers.AddLabel(statusObj, lastPackStatus, 12f,
                    TextAlignmentOptions.MidlineLeft, lastPackStatusColor);
            }

            UIHelpers.EnsureAllHoverable(row);
        }

        void SetPackStatus(string text, Color color) {
            lastPackStatus = text;
            lastPackStatusColor = color;
        }

        void ShowPackPicker() {
            if (selectedUnitId == "presets") return;

            var packs = Engine.PackRegistry.All();
            if (packs.Count == 0) {
                SetPackStatus("pack.none_defined".i18n(), new Color(1f, 0.5f, 0.4f));
                Log.UI.Info("No packs available — create one on the Presets tab first");
                RefreshRuleList();
                return;
            }

            var options = new List<string>();
            foreach (var p in packs) options.Add($"{p.Name} ({p.PresetIds.Count})");

            PopupSelector.ShowPicker(options, idx => {
                if (idx < 0 || idx >= packs.Count) return;
                ApplyPack(packs[idx]);
            });
        }

        // Re-applying is a sync: only members missing from THIS pack's rules get appended,
        // so a user who deleted one rule can restore it without producing duplicates.
        void ApplyPack(TacticsPack pack) {
            var list = selectedUnitId == null
                ? ConfigManager.Current.GlobalRules
                : GetOrCreateCharacterRules(selectedUnitId);

            var plan = Engine.PackRegistry.PlanApply(pack, list,
                presetId => Engine.PresetRegistry.Get(presetId) != null);

            // Computed BEFORE AddRange — otherwise the freshly appended rules count themselves.
            int alreadyPresent = Engine.PackRegistry.CountAlreadyApplied(pack, list);
            list.AddRange(plan);
            ConfigManager.Save();
            SetPackStatus(
                plan.Count == 0
                    ? string.Format("status.pack_nothing_to_add".i18n(), pack.Name)
                    : string.Format("status.pack_applied".i18n(), pack.Name, plan.Count, alreadyPresent),
                plan.Count == 0 ? Color.gray : new Color(0.6f, 0.85f, 0.6f));
            Log.UI.Info($"Applied pack '{pack.Name}': +{plan.Count} rule(s), {alreadyPresent} already present");
            RefreshRuleList();
        }

        // Removes only rules stamped with this pack. Rules the user unlinked (Unlink & Edit
        // clears PresetId but keeps PackId) are removed too — they are still this pack's slot.
        void RemovePackFromList(TacticsPack pack) {
            var list = selectedUnitId == null
                ? ConfigManager.Current.GlobalRules
                : GetOrCreateCharacterRules(selectedUnitId);

            int removed = list.RemoveAll(r => r != null && r.PackId == pack.Id);
            ConfigManager.Save();
            SetPackStatus(string.Format("status.pack_removed".i18n(), removed, pack.Name),
                new Color(0.6f, 0.85f, 0.6f));
            Log.UI.Info($"Removed {removed} rule(s) of pack '{pack.Name}'");
            RefreshRuleList();
        }

        // Turns the whole visible list into a pack: every standalone rule is promoted to a
        // preset (PromoteRuleToPreset links the original in place), already-linked rules
        // contribute their existing preset. The rules stay where they are — the pack is a
        // reusable copy of the list, not a move.
        void SaveListAsPack() {
            if (selectedUnitId == "presets") return;

            var list = selectedUnitId == null
                ? ConfigManager.Current.GlobalRules
                : GetOrCreateCharacterRules(selectedUnitId);

            if (list.Count == 0) {
                SetPackStatus("status.pack_save_list_empty".i18n(), new Color(1f, 0.5f, 0.4f));
                Log.UI.Info("Save list as pack: list is empty");
                RefreshRuleList();
                return;
            }

            var pack = new TacticsPack {
                Name = string.Format("pack.saved_list_name".i18n(),
                    selectedUnitId == null ? "tab.global".i18n() : GetCharacterName(selectedUnitId)),
            };

            int promoted = 0;
            int skipped = 0;
            foreach (var rule in list) {
                if (rule == null) continue;
                string presetId = rule.PresetId;
                if (string.IsNullOrEmpty(presetId)) {
                    var preset = Engine.PresetRegistry.PromoteRuleToPreset(rule);
                    if (preset == null) {
                        // Promotion failed on disk; PromoteRuleToPreset left the rule intact.
                        Log.UI.Warn($"Save list as pack: could not promote rule '{rule.Name}' — skipped");
                        skipped++;
                        continue;
                    }
                    presetId = preset.Id;
                    promoted++;
                }
                // A list may legitimately hold the same preset twice; the pack keeps one slot
                // per preset because PlanApply de-duplicates per pack anyway.
                if (!pack.PresetIds.Contains(presetId)) pack.PresetIds.Add(presetId);
                // Only claim rules that don't belong to a pack yet. Re-stamping a rule that
                // came from another pack would silently steal it: its old chip would vanish
                // and "remove pack X" would no longer find it.
                if (string.IsNullOrEmpty(rule.PackId)) rule.PackId = pack.Id;
            }

            if (pack.PresetIds.Count == 0) {
                SetPackStatus(string.Format("status.save_failed".i18n(), pack.Name), new Color(1f, 0.5f, 0.4f));
                Log.UI.Warn("Save list as pack: no rule could be promoted");
                RefreshRuleList();
                return;
            }

            if (!Engine.PackRegistry.Save(pack)) {
                SetPackStatus(string.Format("status.save_failed".i18n(), pack.Name), new Color(1f, 0.5f, 0.4f));
                Log.UI.Error($"Save list as pack: failed to persist pack '{pack.Name}'");
                // The rules were already promoted and re-linked in memory; persist that much
                // so the user doesn't lose the promotion along with the pack.
                ConfigManager.Save();
                RefreshRuleList();
                return;
            }
            ConfigManager.Save();
            if (skipped > 0) {
                SetPackStatus(
                    string.Format("status.pack_saved_from_list_partial".i18n(), pack.PresetIds.Count, pack.Name, skipped),
                    new Color(1f, 0.8f, 0.4f));
            } else {
                SetPackStatus(
                    string.Format("status.pack_saved_from_list".i18n(), pack.PresetIds.Count, pack.Name),
                    new Color(0.6f, 0.85f, 0.6f));
            }
            Log.UI.Info($"Saved {pack.PresetIds.Count} rule(s) as pack '{pack.Name}' ({promoted} newly promoted, {skipped} skipped)");
            RefreshRuleList();
        }

        static string HudToggleLabel() =>
            (ModSettingsManager.Current.ShowHudButton ? "hud_button.hide" : "hud_button.show").i18n();

        void UpdateToggleLabel() {
            if (toggleLabel == null) return;

            if (selectedUnitId == null) {
                toggleLabel.text = "toggle.global_rules".i18n();
                toggleLabel.color = Color.white;
            } else if (selectedUnitId == "presets") {
                toggleLabel.text = "tab.presets".i18n();
                toggleLabel.color = Color.white;
            } else {
                var config = ConfigManager.Current;
                bool enabled = config.IsEnabled(selectedUnitId);
                var charName = GetCharacterName(selectedUnitId);
                var template = (enabled ? "toggle.tactics.enabled" : "toggle.tactics.disabled").i18n();
                toggleLabel.text = string.Format(template, charName);
                toggleLabel.color = enabled ? Color.white : Color.gray;
                toggleLabel.outlineWidth = 0.25f;
                toggleLabel.outlineColor = new Color32(0, 0, 0, 255);
            }
        }

        void ToggleTactics() {
            if (selectedUnitId == null || selectedUnitId == "presets") return;
            var config = ConfigManager.Current;
            bool current = config.IsEnabled(selectedUnitId);
            config.TacticsEnabled[selectedUnitId] = !current;
            ConfigManager.Save();
            RefreshRuleList();
        }

        void AddNewRule() {
            if (selectedUnitId == "presets") return;
            var config = ConfigManager.Current;
            var rules = selectedUnitId == null
                ? config.GlobalRules
                : GetOrCreateCharacterRules(selectedUnitId);

            rules.Add(new TacticsRule {
                Name = "default.new_rule_name".i18n(),
                Enabled = true,
                ConditionGroups = new List<ConditionGroup> {
                    new ConditionGroup {
                        Conditions = new List<Condition> { new Condition() }
                    }
                }
            });
            ConfigManager.Save();
            RefreshRuleList();
        }

        void AddFromPreset() {
            if (selectedUnitId == "presets") return;

            var presets = WrathTactics.Engine.PresetRegistry.All();
            if (presets.Count == 0) {
                Log.UI.Info("No presets available — create one on the Presets tab first");
                return;
            }

            var options = new List<string>();
            foreach (var p in presets) options.Add(p.Name);

            PopupSelector.ShowPicker(options, idx => {
                if (idx < 0 || idx >= presets.Count) return;
                var preset = presets[idx];
                var list = selectedUnitId == null
                    ? ConfigManager.Current.GlobalRules
                    : GetOrCreateCharacterRules(selectedUnitId);

                list.Add(new TacticsRule {
                    Id = System.Guid.NewGuid().ToString(),
                    Enabled = true,
                    PresetId = preset.Id,
                });
                ConfigManager.Save();
                RefreshRuleList();
            });
        }

        List<TacticsRule> GetOrCreateCharacterRules(string unitId) {
            var config = ConfigManager.Current;
            if (!config.CharacterRules.TryGetValue(unitId, out var rules)) {
                rules = new List<TacticsRule>();
                config.CharacterRules[unitId] = rules;
            }
            return rules;
        }

        string GetCharacterName(string unitId) {
            if (Game.Instance?.Player?.PartyAndPets == null) return unitId;
            foreach (var unit in Game.Instance.Player.PartyAndPets) {
                if (unit.UniqueId == unitId) return unit.CharacterName;
            }
            return unitId;
        }

        float hudButtonRetrySeconds;

        void Update() {
            // Controller players have no cursor to click the button with and asked for a
            // way to remove it — machine-global setting (ModSettingsManager, not the
            // per-save config), toggled on the Global tab. Ctrl+T still opens the panel.
            // Destroy (not SetActive) so the recreate branch below stays the single owner
            // of button lifetime; keep the retry timer zeroed so a later re-enable starts
            // from a clean clock instead of a stale partial accumulation.
            if (!ModSettingsManager.Current.ShowHudButton) {
                if (hudButton != null) { Destroy(hudButton); hudButton = null; }
                hudButtonRetrySeconds = 0f;
            }
            // Re-create only when the button was actually destroyed (BubbleBuffs rebuilds
            // its root on area load and our child gets torn down with it; the hide-toggle
            // branch above is the other path to null). DO NOT recreate on
            // !activeInHierarchy: dialog scenes deactivate the HUD parent briefly, and the
            // per-frame destroy+reparent cycle leaves the button at a transient layout state
            // — observed mid-screen over dialog text. Unity's destroyed-object equality
            // covers the BB-rebuild teardown case.
            else if (hudButton == null && Game.Instance?.UI?.Canvas != null) {
                hudButtonRetrySeconds += Time.deltaTime;
                var canvas = Game.Instance.UI.Canvas.transform;
                // BubbleBuffs is the only container we can rely on as a parent: BB rebuilds
                // its own GridLayoutGroup with explicit cell sizing, so dropping in our
                // helmet adds a visible cell. The vanilla "NestedCanvas1/.../ButtonsPart/
                // Container" looks identical structurally but its layout/sizing makes our
                // child invisible (clipped, off-screen on Steam Deck — observed empirically).
                // Without BB we use the floating fallback at a fixed safe screen position.
                var bbContainer = canvas.Find("BUBBLEMODS_ROOT/IngameMenuView/ButtonsPart/Container");
                if (bbContainer != null) {
                    CreateButtonInGameContainer(bbContainer);
                    hudButtonRetrySeconds = 0f;
                } else if (hudButtonRetrySeconds > 5f) {
                    CreateFloatingHudButton(canvas);
                    hudButtonRetrySeconds = 0f;
                }
            }

            // ESC closes panel when visible
            if (isVisible && Input.GetKeyDown(KeyCode.Escape)) {
                Toggle();
                // Consume the input so the game menu doesn't also open
                Input.ResetInputAxes();
                return;
            }

            // Keyboard shortcut: Ctrl+T
            if (Input.GetKeyDown(KeyCode.T) &&
                (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))) {
                Toggle();
            }
        }

        // Parents a fresh helmet button into the GridLayoutGroup container that hosts the
        // game's HUD buttons. Same code path for BubbleBuffs' rebuilt container and the
        // vanilla NestedCanvas1 container — both have identical structure.
        void CreateButtonInGameContainer(Transform container) {
            if (hudButton != null) { Object.Destroy(hudButton); hudButton = null; }

            Sprite helmetSprite = ResolveHudButtonSprite(Game.Instance.UI.Canvas.transform);

            var btn = new GameObject("TacticsBtn", typeof(RectTransform));
            btn.transform.SetParent(container, false);
            btn.transform.localScale = Vector3.one;
            hudButton = btn;

            var btnImg = btn.AddComponent<Image>();
            if (helmetSprite != null) {
                btnImg.sprite = helmetSprite;
                btnImg.preserveAspect = true;
                btnImg.color = Color.white;
            } else {
                btnImg.color = new Color(0.5f, 0.35f, 0.15f, 1f);
            }
            btnImg.raycastTarget = true;

            var btnComp = btn.AddComponent<Button>();
            btnComp.targetGraphic = btnImg;
            WireHudButtonHover(btnComp, helmetSprite);
            btnComp.onClick.AddListener(() => {
                Log.UI.Debug("HUD button clicked");
                Toggle();
            });

            Log.UI.Info($"HUD button created in {container.parent?.parent?.name ?? "?"} container");
        }

        void CreateFloatingHudButton(Transform canvas) {
            if (hudButton != null) { Object.Destroy(hudButton); hudButton = null; }

            Sprite helmetSprite = ResolveHudButtonSprite(canvas);

            var (btn, btnRect) = UIHelpers.Create("WrathTacticsHudBtn", canvas);
            hudButton = btn;

            // Bottom-LEFT corner with clear margin — avoids overlap with the action bar
            // (bottom-center), the mini-map (bottom-right), and dialog UI (centred). This
            // position holds across all aspect ratios including Steam Deck (1280×800).
            btnRect.anchorMin = new Vector2(0f, 0f);
            btnRect.anchorMax = new Vector2(0f, 0f);
            btnRect.pivot = new Vector2(0f, 0f);
            btnRect.anchoredPosition = new Vector2(20, 120);
            btnRect.sizeDelta = new Vector2(48, 48);

            // Main button background — just the helmet sprite, no extra frame
            var btnImg = btn.AddComponent<Image>();
            if (helmetSprite != null) {
                btnImg.sprite = helmetSprite;
                btnImg.preserveAspect = true;
                btnImg.color = Color.white;
            } else {
                btnImg.color = new Color(0.4f, 0.3f, 0.15f, 0.95f);
            }
            btnImg.raycastTarget = true;

            var btnComp = btn.AddComponent<Button>();
            btnComp.targetGraphic = btnImg;
            WireHudButtonHover(btnComp, helmetSprite);
            btnComp.onClick.AddListener(() => {
                Log.UI.Debug("HUD button clicked!");
                Toggle();
            });

            Log.UI.Info($"HUD button created (helmetSprite={(helmetSprite != null ? "found" : "null")})");
        }

        // Bundled vanilla helmet icon first (Assets/icons/hud_button.png — the same
        // UI_HudIcon_Character_Default sprite the canvas extraction below chases at
        // runtime, but deterministic). Live canvas extraction is only the backup for
        // a missing/corrupt PNG on disk; the flat brown square in the callers is the
        // last resort. Canvas extraction is known to fail in controller/console UI
        // mode (different canvas hierarchy) — that failure shipped as the "brown
        // square in the corner" Nexus report.
        static Sprite ResolveHudButtonSprite(Transform canvas) =>
            ThemeProvider.HudButton != null ? ThemeProvider.HudButton : TryExtractGameSprite(canvas);

        // SpriteSwap hover for the bundled icon pair. No-op when the button ended up
        // with an extracted/null sprite — the default ColorTint transition stays.
        static void WireHudButtonHover(Button btn, Sprite normal) {
            if (normal == null || normal != ThemeProvider.HudButton || ThemeProvider.HudButtonHover == null) return;
            btn.transition = Selectable.Transition.SpriteSwap;
            // selected/disabled left null — SpriteSwap falls back to the Image's own
            // sprite, so the base visual stays authoritative if it ever changes.
            btn.spriteState = new SpriteState {
                highlightedSprite = ThemeProvider.HudButtonHover,
                pressedSprite     = ThemeProvider.HudButtonHover,
            };
        }

        static Sprite TryExtractGameSprite(Transform canvas) {
            // Try to find the Character button sprite (helmet icon)
            string[] candidatePaths = new[] {
                "NestedCanvas1/IngameMenuView/ButtonsPart/Container",
                "BUBBLEMODS_ROOT/IngameMenuView/ButtonsPart/Container"
            };
            foreach (var path in candidatePaths) {
                var container = canvas.Find(path);
                if (container == null) continue;
                for (int i = 0; i < container.childCount; i++) {
                    var child = container.GetChild(i);
                    var imgs = child.GetComponentsInChildren<Image>(true);
                    foreach (var img in imgs) {
                        if (img.sprite != null) return img.sprite;
                    }
                }
            }
            return null;
        }


        static string EffectiveDisplayName(TacticsRule rule) {
            if (rule == null) return "";
            if (!string.IsNullOrEmpty(rule.PresetId)) {
                var preset = Engine.PresetRegistry.Get(rule.PresetId);
                if (preset != null) return preset.Name ?? "";
            }
            return rule.Name ?? "";
        }

        public void HandlePartyCombatStateChanged(bool inCombat) {
            // Could auto-close panel when combat starts
        }

        void OnDestroy() {
            if (panelRoot != null) Destroy(panelRoot);
            if (hudButton != null) Destroy(hudButton);
        }
    }

}
