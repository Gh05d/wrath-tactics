using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.PubSubSystem;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Models;
using WrathTactics.Persistence;

namespace WrathTactics.UI {
    public class TacticsPanel : MonoBehaviour, IPartyCombatHandler {
        static TacticsPanel instance;
        GameObject panelRoot;
        GameObject hudButton;
        bool isVisible;
        bool hudButtonCreated;
        string selectedUnitId; // null = Global, "presets" = Presets
        string lastNonPresetUnitId; // last selected tab that wasn't "presets"
        Transform ruleListContent; // parent for rule cards
        TextMeshProUGUI toggleLabel;
        Transform tabBarTransform; // reference to rebuild tabs

        public static TacticsPanel Instance => instance;

        public static void Install() {
            if (instance != null) return;

            var go = new GameObject("WrathTacticsController");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<TacticsPanel>();
            EventBus.Subscribe(instance);
            Main.Log("[UI] TacticsPanel installed");
        }

        public static void Uninstall() {
            if (instance != null) {
                EventBus.Unsubscribe(instance);
                if (instance.panelRoot != null) Destroy(instance.panelRoot);
                if (instance.hudButton != null) Destroy(instance.hudButton);
                Destroy(instance.gameObject);
                instance = null;
                Main.Log("[UI] TacticsPanel uninstalled");
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
            UIHelpers.AddLabel(titleBar, "Wrath Tactics", 22f, TextAlignmentOptions.Midline);

            // Close button
            var (closeBtn, closeRect) = UIHelpers.Create("CloseButton", titleBar.transform);
            closeRect.SetAnchor(0.95, 1, 0, 1);
            closeRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(closeBtn, new Color(0.6f, 0.2f, 0.2f, 1f));
            UIHelpers.AddLabel(closeBtn, "X", 19f, TextAlignmentOptions.Midline);
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

            // Scrollable rule list
            CreateRuleList(root.transform);

            panelRoot.SetActive(false);
            Main.Log("[UI] Panel created");
        }

        void RebuildTabs() {
            if (tabBarTransform == null) return;

            // Clear existing tabs
            for (int i = tabBarTransform.childCount - 1; i >= 0; i--)
                Destroy(tabBarTransform.GetChild(i).gameObject);

            // Global tab
            AddTab(tabBarTransform.gameObject, "Global", () => SelectTab(null));

            // Party member tabs
            if (Game.Instance?.Player?.Party != null) {
                foreach (var unit in Game.Instance.Player.Party) {
                    if (!unit.IsInGame) continue;
                    var uid = unit.UniqueId;
                    AddTab(tabBarTransform.gameObject, unit.CharacterName, () => SelectTab(uid));
                }
            }

            // Presets tab
            AddTab(tabBarTransform.gameObject, "Presets", () => SelectTab("presets"));
        }

        void AddTab(GameObject parent, string label, UnityEngine.Events.UnityAction onClick) {
            var (btn, _) = UIHelpers.Create($"Tab_{label}", parent.transform);
            UIHelpers.AddBackground(btn, new Color(0.25f, 0.2f, 0.15f, 1f));
            UIHelpers.AddLabel(btn, label, 14f, TextAlignmentOptions.Midline);
            btn.AddComponent<Button>().onClick.AddListener(onClick);
        }

        void SelectTab(string unitId) {
            if (selectedUnitId != "presets")
                lastNonPresetUnitId = selectedUnitId;
            selectedUnitId = unitId;
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
            toggleLabel = UIHelpers.AddLabel(toggleBtn, "Global Rules", 16f,
                TextAlignmentOptions.MidlineLeft, Color.white);
            toggleBtn.AddComponent<Button>().onClick.AddListener(ToggleTactics);

            // Add Rule button
            var (addBtn, addRect) = UIHelpers.Create("AddRuleBtn", row.transform);
            addRect.SetAnchor(0.75, 1, 0, 1);
            addRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(addBtn, new Color(0.2f, 0.4f, 0.2f, 1f));
            UIHelpers.AddLabel(addBtn, "+ New Rule", 16f, TextAlignmentOptions.Midline);
            addBtn.AddComponent<Button>().onClick.AddListener(AddNewRule);
        }

        void CreateRuleList(Transform parent) {
            // ScrollRect container
            var (scrollObj, scrollRect) = UIHelpers.Create("RuleScroll", parent);
            scrollRect.SetAnchor(0.01, 0.99, 0.02, 0.76);
            scrollRect.sizeDelta = Vector2.zero;

            // Viewport with RectMask2D instead of Mask
            var (viewport, viewportRect) = UIHelpers.Create("Viewport", scrollObj.transform);
            viewportRect.FillParent();
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

            // Wire ScrollRect
            var scroll = scrollObj.AddComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;

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
                presetPanel.Init(lastNonPresetUnitId, ruleListContent, () => {
                    // After loading a preset, switch back to the character tab
                    selectedUnitId = lastNonPresetUnitId;
                    RefreshRuleList();
                });
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

        void CreateHudButton() {
            if (hudButton != null) return;

            var canvas = Game.Instance.UI.Canvas.transform;

            var hudLayout = canvas.Find("NestedCanvas1");
            if (hudLayout == null) {
                Main.Log("[UI] NestedCanvas1 not found — using fallback button");
                CreateFallbackHudButton(canvas);
                return;
            }

            var buttonContainer = hudLayout.Find("IngameMenuView/ButtonsPart/Container");
            if (buttonContainer == null) {
                Main.Log("[UI] IngameMenuView/ButtonsPart/Container not found under NestedCanvas1");
                var childNames = new System.Text.StringBuilder();
                for (int i = 0; i < hudLayout.childCount; i++) {
                    if (i > 0) childNames.Append(", ");
                    childNames.Append(hudLayout.GetChild(i).name);
                }
                Main.Log($"[UI] NestedCanvas1 children: {childNames}");
                CreateFallbackHudButton(canvas);
                return;
            }

            // Try to clone an existing button for consistent styling
            var existingButton = buttonContainer.childCount > 0
                ? buttonContainer.GetChild(0).gameObject : null;
            if (existingButton != null) {
                var cloned = UnityEngine.Object.Instantiate(existingButton, buttonContainer);
                cloned.name = "WrathTacticsHudBtn";
                hudButton = cloned;

                var btn = cloned.GetComponent<Button>();
                if (btn != null) {
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(Toggle);
                }

                var label = cloned.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null) label.text = "Tactics";

                Main.Log("[UI] HUD button created (cloned from game UI)");
                return;
            }

            Main.Log("[UI] No existing buttons in container to clone — using fallback");
            CreateFallbackHudButton(canvas);
        }

        void CreateFallbackHudButton(Transform canvas) {
            var (btn, btnRect) = UIHelpers.Create("WrathTacticsHudBtn", canvas);
            hudButton = btn;
            btnRect.SetAnchor(0, 0, 0, 0);
            btnRect.pivot = new Vector2(0, 0);
            btnRect.anchoredPosition = new Vector2(15, 150);
            btnRect.sizeDelta = new Vector2(50, 50);
            UIHelpers.AddBackground(btn, new Color(0.35f, 0.25f, 0.15f, 0.95f));
            UIHelpers.AddLabel(btn, "T", 24f, TextAlignmentOptions.Center);
            btn.AddComponent<Button>().onClick.AddListener(Toggle);
            Main.Log("[UI] Fallback HUD button created");
        }

        void Update() {
            // Create HUD button once the game UI is ready
            if (!hudButtonCreated && Game.Instance?.UI?.Canvas != null) {
                CreateHudButton();
                hudButtonCreated = true;
            }

            // Keyboard shortcut: Ctrl+T
            if (Input.GetKeyDown(KeyCode.T) &&
                (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))) {
                Toggle();
            }
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
