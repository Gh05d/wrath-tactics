using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.PubSubSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
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

        public static TacticsPanel Instance => instance;

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
            if (panelRoot == null) CreatePanel();
            isVisible = !isVisible;
            panelRoot.SetActive(isVisible);
            if (isVisible) {
                RebuildTabs();
                RefreshRuleList();
            }
        }

        void CreatePanel() {
            var canvas = Game.Instance.UI.Canvas.transform;

            // Main panel container — percentage-based: 70% wide, 80% tall
            var (root, rootRect) = UIHelpers.Create("WrathTacticsPanel", canvas);
            panelRoot = root;

            rootRect.SetAnchor(0.15, 0.85, 0.1, 0.9);
            rootRect.sizeDelta = Vector2.zero;

            // Dark background
            UIHelpers.AddBackground(root, new Color(0.1f, 0.1f, 0.1f, 0.95f));

            // Title bar
            var (titleBar, titleRect) = UIHelpers.Create("TitleBar", root.transform);
            titleRect.SetAnchor(0, 1, 0.92, 1);
            titleRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(titleBar, new Color(0.2f, 0.15f, 0.1f, 1f));
            UIHelpers.AddLabel(titleBar, "Wrath Tactics", 26f, TextAlignmentOptions.Midline);

            // Close button
            var (closeBtn, closeRect) = UIHelpers.Create("CloseButton", titleBar.transform);
            closeRect.SetAnchor(0.95, 1, 0, 1);
            closeRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(closeBtn, new Color(0.6f, 0.2f, 0.2f, 1f));
            UIHelpers.AddLabel(closeBtn, "X", 22f, TextAlignmentOptions.Midline);
            closeBtn.AddComponent<Button>().onClick.AddListener(Toggle);

            // Tab bar
            var (tabBar, tabRect) = UIHelpers.Create("TabBar", root.transform);
            tabRect.SetAnchor(0, 1, 0.84, 0.91);
            tabRect.sizeDelta = Vector2.zero;
            tabBarTransform = tabBar.transform;

            var hlg = tabBar.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 4;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;

            RebuildTabs();

            // Toggle + Add rule row
            CreateControlRow(root.transform);

            // Filter strip (sticky — stays above the scroll area regardless of tab)
            CreateFilterStrip(root.transform);

            // Scrollable rule list
            CreateRuleList(root.transform);

            // Empty-state label for the rule list (hidden by default, driven by ApplyFilter)
            CreateRuleFilterEmptyLabel(root.transform);

            panelRoot.SetActive(false);
            Log.UI.Info("Panel created");
        }

        void RebuildTabs() {
            if (tabBarTransform == null) return;

            // Clear existing tabs
            for (int i = tabBarTransform.childCount - 1; i >= 0; i--)
                Destroy(tabBarTransform.GetChild(i).gameObject);

            // Global tab
            AddTab(tabBarTransform.gameObject, "Global", null, () => SelectTab(null));

            // Party member tabs
            if (Game.Instance?.Player?.Party != null) {
                foreach (var unit in Game.Instance.Player.Party) {
                    if (!unit.IsInGame) continue;
                    var uid = unit.UniqueId;
                    AddTab(tabBarTransform.gameObject, unit.CharacterName, uid, () => SelectTab(uid));
                }
            }

            // Presets tab
            AddTab(tabBarTransform.gameObject, "Presets", "presets", () => SelectTab("presets"));
        }

        static readonly Color TabNormal = new Color(0.25f, 0.2f, 0.15f, 1f);
        static readonly Color TabSelected = new Color(0.4f, 0.3f, 0.15f, 1f);

        void AddTab(GameObject parent, string label, string tabId, UnityEngine.Events.UnityAction onClick) {
            var (btn, _) = UIHelpers.Create($"Tab_{label}", parent.transform);
            bool isSelected = (tabId == null && selectedUnitId == null)
                || (tabId != null && tabId == selectedUnitId);
            UIHelpers.AddBackground(btn, isSelected ? TabSelected : TabNormal);
            UIHelpers.AddLabel(btn, label, 16f, TextAlignmentOptions.Midline);
            btn.AddComponent<Button>().onClick.AddListener(onClick);
        }

        void SelectTab(string unitId) {
            if (selectedUnitId != "presets")
                lastNonPresetUnitId = selectedUnitId;
            selectedUnitId = unitId;
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
            toggleLabel = UIHelpers.AddLabel(toggleBtn, "Global Rules", 18f,
                TextAlignmentOptions.MidlineLeft, Color.white);
            toggleBtn.AddComponent<Button>().onClick.AddListener(ToggleTactics);

            // "+ New Rule" button
            var (addBtn, addRect) = UIHelpers.Create("AddRuleBtn", row.transform);
            addRect.SetAnchor(0.55, 0.76, 0, 1);
            addRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(addBtn, new Color(0.2f, 0.4f, 0.2f, 1f));
            UIHelpers.AddLabel(addBtn, "+ New Rule", 18f, TextAlignmentOptions.Midline);
            addBtn.AddComponent<Button>().onClick.AddListener(AddNewRule);

            // "+ From Preset" button
            var (fromPresetBtn, fromPresetRect) = UIHelpers.Create("FromPresetBtn", row.transform);
            fromPresetRect.SetAnchor(0.77, 1, 0, 1);
            fromPresetRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(fromPresetBtn, new Color(0.2f, 0.35f, 0.5f, 1f));
            UIHelpers.AddLabel(fromPresetBtn, "+ From Preset \u25be", 16f, TextAlignmentOptions.Midline);
            fromPresetBtn.AddComponent<Button>().onClick.AddListener(AddFromPreset);
        }

        void CreateFilterStrip(Transform parent) {
            var (strip, stripRect) = UIHelpers.Create("FilterStrip", parent);
            stripRect.SetAnchor(0.01, 0.99, 0.72, 0.76);
            stripRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(strip, new Color(0.14f, 0.14f, 0.14f, 1f));

            ruleFilterInput = UIHelpers.CreateTMPInputField(strip, "FilterInput",
                0.02, 0.85, "", 15f,
                placeholderText: "Filter rules…");
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
            ruleFilterClearButton.interactable = false;
        }

        void UpdateFilterClearButton() {
            if (ruleFilterClearButton == null) return;
            ruleFilterClearButton.interactable = !string.IsNullOrEmpty(currentRuleFilter);
        }

        void CreateRuleFilterEmptyLabel(Transform parent) {
            var (obj, rect) = UIHelpers.Create("RuleFilterEmpty", parent);
            // Same anchor as the rule scroll so the label overlays its center
            rect.SetAnchor(0.01, 0.99, 0.02, 0.71);
            rect.sizeDelta = Vector2.zero;
            UIHelpers.AddLabel(obj, "No matching rules", 16f,
                TextAlignmentOptions.Midline, new Color(0.6f, 0.6f, 0.6f));
            obj.SetActive(false);
            ruleFilterEmptyLabel = obj;
        }

        void ApplyFilter() {
            // Filled in by the next two tasks; stub for now so the input listener compiles.
            if (ruleFilterEmptyLabel != null) ruleFilterEmptyLabel.SetActive(false);
        }

        void CreateRuleList(Transform parent) {
            // ScrollRect container
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
            UIHelpers.AddBackground(scrollbarObj, new Color(0.15f, 0.15f, 0.15f, 0.85f));

            var (handleObj, handleRect) = UIHelpers.Create("Handle", scrollbarObj.transform);
            handleRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(handleObj, new Color(0.7f, 0.7f, 0.7f, 1.0f));

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

            // Clear existing cards
            for (int i = ruleListContent.childCount - 1; i >= 0; i--)
                Destroy(ruleListContent.GetChild(i).gameObject);

            if (selectedUnitId == "presets") {
                var (presetObj, _) = UIHelpers.Create("PresetPanel", ruleListContent);
                var presetPanel = presetObj.AddComponent<PresetPanel>();
                presetPanel.Init(lastNonPresetUnitId, ruleListContent, () => RefreshRuleList());
                UpdateToggleLabel();
                return;
            }

            var config = ConfigManager.Current;
            var rules = selectedUnitId == null
                ? config.GlobalRules
                : config.GetRulesForCharacter(selectedUnitId);

            UpdateToggleLabel();

            for (int i = 0; i < rules.Count; i++) {
                var (card, _) = UIHelpers.Create($"Rule_{i}", ruleListContent);
                var widget = card.AddComponent<RuleEditorWidget>();
                var capturedRules = rules;
                widget.Init(rules[i], i, capturedRules, () => RefreshRuleList(), selectedUnitId);
            }
        }

        void UpdateToggleLabel() {
            if (toggleLabel == null) return;

            if (selectedUnitId == null) {
                toggleLabel.text = "Global Rules";
                toggleLabel.color = Color.white;
            } else if (selectedUnitId == "presets") {
                toggleLabel.text = "Presets";
                toggleLabel.color = Color.white;
            } else {
                var config = ConfigManager.Current;
                bool enabled = config.IsEnabled(selectedUnitId);
                var charName = GetCharacterName(selectedUnitId);
                toggleLabel.text = $"Tactics {(enabled ? "enabled" : "disabled")} for {charName}";
                toggleLabel.color = enabled ? Color.green : Color.gray;
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
                Name = "New Rule",
                Priority = rules.Count,
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
                    Priority = list.Count,
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
            if (Game.Instance?.Player?.Party == null) return unitId;
            foreach (var unit in Game.Instance.Player.Party) {
                if (unit.UniqueId == unitId) return unit.CharacterName;
            }
            return unitId;
        }

        float hudButtonRetrySeconds;

        void Update() {
            // Re-create button if it was destroyed (e.g. by BubbleBuffs rebuilding its root on area load)
            bool needsButton = hudButton == null || !hudButton.activeInHierarchy;
            if (needsButton && Game.Instance?.UI?.Canvas != null) {
                hudButtonRetrySeconds += Time.deltaTime;
                var canvas = Game.Instance.UI.Canvas.transform;
                var bbContainer = canvas.Find("BUBBLEMODS_ROOT/IngameMenuView/ButtonsPart/Container");
                if (bbContainer != null) {
                    CreateButtonInBubbleBuffsContainer(bbContainer);
                    hudButtonRetrySeconds = 0f;
                } else if (hudButtonRetrySeconds > 30f && hudButton == null) {
                    CreateFloatingHudButton(canvas);
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

        void CreateButtonInBubbleBuffsContainer(Transform container) {
            if (hudButton != null) { Object.Destroy(hudButton); hudButton = null; }

            // Extract helmet sprite from game's own HUD
            Sprite helmetSprite = TryExtractGameSprite(Game.Instance.UI.Canvas.transform);

            // Create fresh button inside the GridLayoutGroup container
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
            btnComp.onClick.AddListener(() => {
                Log.UI.Debug("HUD button clicked (in BB container)");
                Toggle();
            });

            Log.UI.Info("HUD button created in BubbleBuffs container");
        }

        void CreateFloatingHudButton(Transform canvas) {
            if (hudButton != null) { Object.Destroy(hudButton); hudButton = null; }

            // Try to extract a helmet sprite from the Character button in the HUD
            Sprite helmetSprite = TryExtractGameSprite(canvas);

            var (btn, btnRect) = UIHelpers.Create("WrathTacticsHudBtn", canvas);
            hudButton = btn;

            // Position: bottom-center, next to BubbleBuffs row (which sits at roughly y=96)
            // Anchor bottom-center so it scales nicely across resolutions
            btnRect.anchorMin = new Vector2(0.5f, 0f);
            btnRect.anchorMax = new Vector2(0.5f, 0f);
            btnRect.pivot = new Vector2(0.5f, 0f);
            // X offset: to the LEFT of the game's action bar center
            // Y offset: ~96 matches BubbleBuffs row height
            btnRect.anchoredPosition = new Vector2(-260, 96);
            btnRect.sizeDelta = new Vector2(44, 44);

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
            btnComp.onClick.AddListener(() => {
                Log.UI.Debug("HUD button clicked!");
                Toggle();
            });

            Log.UI.Info($"HUD button created (helmetSprite={(helmetSprite != null ? "found" : "null")})");
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
