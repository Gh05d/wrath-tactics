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
using Object = UnityEngine.Object;

namespace WrathTactics.UI {
    public class TacticsPanel : MonoBehaviour, IPartyCombatHandler {
        static TacticsPanel instance;
        GameObject panelRoot;
        GameObject hudButton;
        bool isVisible;
        bool hudButtonCreated;
        float hudButtonSearchTime;
        const float HUD_BUTTON_SEARCH_TIMEOUT = 60f;
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

        void Update() {
            // Keep trying to find BUBBLEMODS_ROOT for up to 60 seconds
            if (!hudButtonCreated && Game.Instance?.UI?.Canvas != null) {
                hudButtonSearchTime += Time.deltaTime;

                var staticRoot = Game.Instance.UI.Canvas.transform;
                var bubbleRoot = staticRoot.Find("BUBBLEMODS_ROOT");

                if (bubbleRoot != null) {
                    // Found BubbleBuffs! Place our button above its container
                    CreateHudButtonAboveBB(bubbleRoot);
                    hudButtonCreated = true;
                } else if (hudButtonSearchTime > HUD_BUTTON_SEARCH_TIMEOUT) {
                    // Timeout — create standalone button
                    CreateStandaloneHudButton(staticRoot);
                    hudButtonCreated = true;
                }
            }

            // Keyboard shortcut: Ctrl+T
            if (Input.GetKeyDown(KeyCode.T) &&
                (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))) {
                Toggle();
            }
        }

        void CreateHudButtonAboveBB(Transform bubbleRoot) {
            if (hudButton != null) { Object.Destroy(hudButton); hudButton = null; }

            var staticRoot = Game.Instance.UI.Canvas.transform;
            var nestedCanvas = staticRoot.Find("NestedCanvas1/");
            if (nestedCanvas == null) {
                Main.Log("[UI] NestedCanvas1 not found, falling back to standalone");
                CreateStandaloneHudButton(staticRoot);
                return;
            }

            // Clone NestedCanvas1 — same approach as BubbleBuffs
            var clone = Instantiate(nestedCanvas.gameObject, nestedCanvas.parent);
            hudButton = clone; // track for cleanup
            clone.name = "WRATHTACTICS_ROOT";

            var cloneRect = clone.transform as RectTransform;
            // Position ABOVE BubbleBuffs (which is at Y=96)
            cloneRect.anchoredPosition = new Vector2(0, 144);
            cloneRect.SetSiblingIndex(nestedCanvas.GetSiblingIndex() + 1);

            // Remove interfering MonoBehaviour components
            foreach (var comp in clone.GetComponents<MonoBehaviour>()) {
                var typeName = comp.GetType().Name;
                if (typeName != "RectTransform" && typeName != "Canvas" && typeName != "CanvasScaler"
                    && typeName != "GraphicRaycaster" && typeName != "CanvasGroup") {
                    DestroyImmediate(comp);
                }
            }

            // Destroy all children except IngameMenuView
            var toDestroy = new List<GameObject>();
            for (int i = 0; i < clone.transform.childCount; i++) {
                if (clone.transform.GetChild(i).name != "IngameMenuView")
                    toDestroy.Add(clone.transform.GetChild(i).gameObject);
            }
            foreach (var obj in toDestroy) Destroy(obj);

            var ingameMenu = clone.transform.Find("IngameMenuView");
            if (ingameMenu == null) { Destroy(clone); CreateStandaloneHudButton(staticRoot); return; }

            // Remove IngameMenuPCView and other MonoBehaviours
            foreach (var comp in ingameMenu.GetComponents<MonoBehaviour>()) {
                DestroyImmediate(comp);
            }

            // Destroy CompassPart
            var compass = ingameMenu.Find("CompassPart");
            if (compass != null) Destroy(compass.gameObject);

            var buttonsPart = ingameMenu.Find("ButtonsPart");
            if (buttonsPart == null) { Destroy(clone); CreateStandaloneHudButton(staticRoot); return; }

            // Destroy TBMMultiButton, InventoryButton, Background
            foreach (var name in new[] { "TBMMultiButton", "InventoryButton", "Background" }) {
                var child = buttonsPart.Find(name);
                if (child != null) Destroy(child.gameObject);
            }

            var container = buttonsPart.Find("Container");
            if (container == null || container.childCount == 0) {
                Destroy(clone); CreateStandaloneHudButton(staticRoot); return;
            }

            var containerRect = container as RectTransform;
            containerRect.anchoredPosition = Vector2.zero;
            containerRect.sizeDelta = new Vector2(47.7f, containerRect.sizeDelta.y); // Width for 1 button

            var grid = container.GetComponent<GridLayoutGroup>();
            if (grid != null) grid.startCorner = GridLayoutGroup.Corner.LowerLeft;

            // Keep child[0] as prefab, destroy rest
            var prefab = container.GetChild(0).gameObject;
            prefab.SetActive(false);
            int childCount = container.childCount;
            for (int i = 1; i < childCount; i++)
                DestroyImmediate(container.GetChild(1).gameObject);

            // Create our button from the prefab
            var btn = Instantiate(prefab, container);
            btn.name = "TacticsBtn";
            btn.SetActive(true);

            // Remove the OwlcatButton — it doesn't work after cloning
            var owlBtns = btn.GetComponentsInChildren<OwlcatButton>(true);
            foreach (var ob in owlBtns) DestroyImmediate(ob);

            // Add simple Unity Button that definitely catches clicks
            var img = btn.GetComponentInChildren<Image>();
            if (img != null) {
                img.raycastTarget = true;
                var unityBtn = img.gameObject.AddComponent<Button>();
                unityBtn.targetGraphic = img;
                unityBtn.onClick.AddListener(() => Toggle());
            }

            // Try to get a game icon sprite (character button icon or similar)
            try {
                // Use a recognizable game sprite — the "Settings" gear icon
                var settingsIcon = Game.Instance.UI.Canvas.transform
                    .Find("NestedCanvas1/IngameMenuView/ButtonsPart/Container")
                    ?.GetChild(0)?.GetComponentInChildren<Image>()?.sprite;
                if (settingsIcon != null && img != null) {
                    img.sprite = settingsIcon;
                    img.color = new Color(0.8f, 0.65f, 0.3f, 1f); // golden tint
                }
            } catch { }

            // Add visibility sync with original NestedCanvas1
            ingameMenu.gameObject.AddComponent<TacticsHudSync>();

            Main.Log("[UI] HUD button created above BubbleBuffs row");
        }

        void CreateStandaloneHudButton(Transform canvas) {
            var (btn, btnRect) = UIHelpers.Create("WrathTacticsHudBtn", canvas);
            hudButton = btn;
            btnRect.SetAnchor(0, 0, 0, 0);
            btnRect.pivot = new Vector2(0, 0);
            btnRect.anchoredPosition = new Vector2(15, 200);
            btnRect.sizeDelta = new Vector2(110, 36);
            UIHelpers.AddBackground(btn, new Color(0.3f, 0.22f, 0.12f, 0.95f));
            UIHelpers.AddLabel(btn, "Tactics", 20f, TMPro.TextAlignmentOptions.Center);
            btn.AddComponent<Button>().onClick.AddListener(Toggle);
            Main.Log("[UI] Standalone HUD button created");
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
        CanvasGroup src;
        Transform tacticsRoot;

        void Start() {
            src = GetComponent<CanvasGroup>();
            tacticsRoot = transform.root.Find("WRATHTACTICS_ROOT") ?? transform.parent;
        }

        void Update() {
            if (src == null || tacticsRoot == null) return;
            tacticsRoot.gameObject.SetActive(src.alpha > 0.5f);
        }
    }

}
