using System;
using System.Collections.Generic;
using Kingmaker;
using Kingmaker.PubSubSystem;
using Owlcat.Runtime.UI.Controls.Button;
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
            UIHelpers.AddLabel(btn, label, 16f, TextAlignmentOptions.Midline);
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
            toggleLabel = UIHelpers.AddLabel(toggleBtn, "Global Rules", 18f,
                TextAlignmentOptions.MidlineLeft, Color.white);
            toggleBtn.AddComponent<Button>().onClick.AddListener(ToggleTactics);

            // Add Rule button
            var (addBtn, addRect) = UIHelpers.Create("AddRuleBtn", row.transform);
            addRect.SetAnchor(0.75, 1, 0, 1);
            addRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(addBtn, new Color(0.2f, 0.4f, 0.2f, 1f));
            UIHelpers.AddLabel(addBtn, "+ New Rule", 18f, TextAlignmentOptions.Midline);
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

            var staticRoot = Game.Instance.UI.Canvas.transform;

            // Path 1: BubbleBuffs is installed — add button to its container
            var bubbleRoot = FindInParent(staticRoot, "BUBBLEMODS_ROOT");
            if (bubbleRoot != null) {
                var container = bubbleRoot.Find("IngameMenuView/ButtonsPart/Container");
                if (container != null) {
                    CreateButtonInContainer(container);
                    // Widen container to fit one more button
                    var containerRect = container as RectTransform;
                    containerRect.sizeDelta = new Vector2(
                        containerRect.sizeDelta.x + 47.7f,
                        containerRect.sizeDelta.y);
                    Main.Log("[UI] HUD button added to BubbleBuffs container");
                    return;
                }
            }

            // Path 2: Standalone — clone NestedCanvas1 ourselves
            var nestedCanvas = staticRoot.Find("NestedCanvas1");
            if (nestedCanvas == null) {
                Main.Log("[UI] NestedCanvas1 not found, cannot create HUD button");
                return;
            }

            var hudObj = nestedCanvas.gameObject;

            // Check for existing clone
            var existingClone = FindInParent(staticRoot, "WRATHTACTICS_ROOT");
            if (existingClone != null) {
                GameObject.Destroy(existingClone.gameObject);
            }

            // Clone NestedCanvas1
            var clone = GameObject.Instantiate(hudObj, hudObj.transform.parent);
            clone.name = "WRATHTACTICS_ROOT";
            var cloneRect = clone.transform as RectTransform;

            // Position above the original HUD (Y=96 like BubbleBuffs would)
            cloneRect.anchoredPosition = new Vector2(0, 96);
            cloneRect.SetSiblingIndex(hudObj.transform.GetSiblingIndex() + 1);

            // Remove components that interfere
            foreach (var comp in clone.GetComponents<MonoBehaviour>()) {
                var typeName = comp.GetType().Name;
                if (typeName == "UISectionHUDController" || typeName.Contains("HUD")) {
                    GameObject.DestroyImmediate(comp);
                }
            }

            var cloneTransform = clone.transform;

            // Destroy all children except IngameMenuView
            var toDestroy = new System.Collections.Generic.List<GameObject>();
            for (int i = 0; i < cloneTransform.childCount; i++) {
                var child = cloneTransform.GetChild(i);
                if (child.name != "IngameMenuView")
                    toDestroy.Add(child.gameObject);
            }
            foreach (var obj in toDestroy) GameObject.Destroy(obj);

            // Clean up IngameMenuView
            var ingameMenu = cloneTransform.Find("IngameMenuView");
            if (ingameMenu == null) {
                Main.Log("[UI] IngameMenuView not found in clone");
                GameObject.Destroy(clone);
                return;
            }

            // Remove IngameMenuPCView component
            foreach (var comp in ingameMenu.GetComponents<MonoBehaviour>()) {
                if (comp.GetType().Name == "IngameMenuPCView")
                    GameObject.DestroyImmediate(comp);
            }

            // Destroy CompassPart if exists
            var compassPart = ingameMenu.Find("CompassPart");
            if (compassPart != null) GameObject.Destroy(compassPart.gameObject);

            // Get ButtonsPart
            var buttonsPart = ingameMenu.Find("ButtonsPart");
            if (buttonsPart == null) {
                Main.Log("[UI] ButtonsPart not found");
                GameObject.Destroy(clone);
                return;
            }

            // Destroy TBMMultiButton, InventoryButton, Background
            var tbm = buttonsPart.Find("TBMMultiButton");
            if (tbm != null) GameObject.Destroy(tbm.gameObject);
            var inv = buttonsPart.Find("InventoryButton");
            if (inv != null) GameObject.Destroy(inv.gameObject);
            var bg = buttonsPart.Find("Background");
            if (bg != null) GameObject.Destroy(bg.gameObject);

            // Configure Container
            var container2 = buttonsPart.Find("Container");
            if (container2 == null) {
                Main.Log("[UI] Container not found in ButtonsPart");
                GameObject.Destroy(clone);
                return;
            }

            var containerRect2 = container2 as RectTransform;
            containerRect2.anchoredPosition = Vector2.zero;
            containerRect2.sizeDelta = new Vector2(47.7f * 2, containerRect2.sizeDelta.y);

            var grid = container2.GetComponent<UnityEngine.UI.GridLayoutGroup>();
            if (grid != null) grid.startCorner = UnityEngine.UI.GridLayoutGroup.Corner.LowerLeft;

            // Keep child[0] as prefab, destroy rest
            var containerGO = container2.gameObject;
            if (containerGO.transform.childCount > 0) {
                var prefab = containerGO.transform.GetChild(0).gameObject;
                prefab.SetActive(false);

                int childCount = containerGO.transform.childCount;
                for (int i = 1; i < childCount; i++) {
                    GameObject.DestroyImmediate(containerGO.transform.GetChild(1).gameObject);
                }

                CreateButtonInContainer(container2);
            }

            // Add visibility sync
            ingameMenu.gameObject.AddComponent<TacticsHudSync>();

            Main.Log("[UI] HUD button created via NestedCanvas1 clone (WRATHTACTICS_ROOT)");
        }

        void CreateButtonInContainer(Transform container) {
            if (container.childCount == 0) return;

            var prefab = container.GetChild(0).gameObject;
            hudButton = GameObject.Instantiate(prefab, container);
            hudButton.name = "TacticsBtn";
            hudButton.SetActive(true);

            // Wire click handler via OwlcatButton
            var owlBtn = hudButton.GetComponentInChildren<OwlcatButton>();
            if (owlBtn != null) {
                owlBtn.OnLeftClick.RemoveAllListeners();
                owlBtn.OnLeftClick.AddListener(() => Toggle());
            }

            // Set icon — create a simple colored sprite
            var images = hudButton.GetComponentsInChildren<UnityEngine.UI.Image>(true);
            if (images.Length > 0) {
                var tex = new Texture2D(64, 64);
                var colors = new Color[64 * 64];
                for (int i = 0; i < colors.Length; i++)
                    colors[i] = new Color(0.5f, 0.35f, 0.15f, 1f);
                tex.SetPixels(colors);
                tex.Apply();
                images[0].sprite = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
            }
        }

        static Transform FindInParent(Transform child, string name) {
            if (child.parent == null) return null;
            return child.parent.Find(name);
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

    class TacticsHudSync : MonoBehaviour {
        void Update() {
            var root = transform.parent?.Find("WRATHTACTICS_ROOT");
            if (root == null) return;

            var cg = GetComponent<CanvasGroup>();
            if (cg == null) return;

            root.gameObject.SetActive(cg.alpha > 0.5f);
        }
    }
}
