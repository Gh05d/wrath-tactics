using System;
using Kingmaker;
using Kingmaker.PubSubSystem;
using UnityEngine;
using UnityEngine.UI;

namespace WrathTactics.UI {
    public class TacticsPanel : MonoBehaviour, IPartyCombatHandler {
        static TacticsPanel instance;
        GameObject panelRoot;
        bool isVisible;

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
                Destroy(instance.gameObject);
                instance = null;
                Main.Log("[UI] TacticsPanel uninstalled");
            }
        }

        public void Toggle() {
            if (panelRoot == null) CreatePanel();
            isVisible = !isVisible;
            panelRoot.SetActive(isVisible);
        }

        void CreatePanel() {
            var canvas = Game.Instance.UI.Canvas.transform;

            // Main panel container
            var (root, rootRect) = UIHelpers.Create("WrathTacticsPanel", canvas);
            panelRoot = root;

            // Position: center of screen, 800x600
            rootRect.SetAnchor(0.5, 0.5, 0.5, 0.5);
            rootRect.sizeDelta = new Vector2(800, 600);

            // Dark background
            UIHelpers.AddBackground(root, new Color(0.1f, 0.1f, 0.1f, 0.95f));

            // Title bar
            var (titleBar, titleRect) = UIHelpers.Create("TitleBar", root.transform);
            titleRect.SetAnchor(0, 1, 0.92, 1);
            titleRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(titleBar, new Color(0.2f, 0.15f, 0.1f, 1f));
            UIHelpers.AddLabel(titleBar, "Wrath Tactics", 18, TextAnchor.MiddleCenter);

            // Close button
            var (closeBtn, closeRect) = UIHelpers.Create("CloseButton", titleBar.transform);
            closeRect.SetAnchor(0.95, 1, 0, 1);
            closeRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(closeBtn, new Color(0.6f, 0.2f, 0.2f, 1f));
            UIHelpers.AddLabel(closeBtn, "X", 16, TextAnchor.MiddleCenter);
            closeBtn.AddComponent<Button>().onClick.AddListener(Toggle);

            // Content area placeholder
            var (content, contentRect) = UIHelpers.Create("Content", root.transform);
            contentRect.SetAnchor(0.02, 0.98, 0.02, 0.9);
            contentRect.sizeDelta = Vector2.zero;
            UIHelpers.AddLabel(content, "Wrath Tactics panel loaded. Rule editor coming soon.", 14);

            panelRoot.SetActive(false);
            Main.Log("[UI] Panel created");
        }

        void Update() {
            if (Input.GetKeyDown(KeyCode.F8)) {
                Toggle();
            }
        }

        public void HandlePartyCombatStateChanged(bool inCombat) {
            // Could auto-close panel when combat starts
        }

        void OnDestroy() {
            if (panelRoot != null) Destroy(panelRoot);
        }
    }
}
