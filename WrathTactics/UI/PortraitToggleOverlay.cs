using System.Collections.Generic;
using Kingmaker;
using Kingmaker.UI.MVVM._ConsoleView.Party;
using Kingmaker.UI.MVVM._PCView.Party;
using Kingmaker.UI.MVVM._VM.Party;
using Owlcat.Runtime.UI.MVVM;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Persistence;

namespace WrathTactics.UI {
    // Attaches PortraitToggleBadge to every party portrait cell. Discovery is
    // component-type based (no stable name-path exists — cells are prefab
    // clones) and throttled; per-frame work is only the cheap state refresh.
    // Covers both the PC and the console/gamepad view tree via the shared
    // ViewBase<PartyCharacterVM> base.
    public static class PortraitToggleOverlay {
        const float DiscoveryInterval = 1f;
        static float discoveryTimer;
        static readonly List<PortraitToggleBadge> badges = new List<PortraitToggleBadge>();

        public static void Sync(float delta) {
            if (Game.Instance?.UI?.Canvas == null) return;

            discoveryTimer -= delta;
            if (discoveryTimer <= 0f) {
                discoveryTimer = DiscoveryInterval;
                Discover();
            }

            bool show = ConfigManager.Current.ShowPortraitToggles;
            for (int i = badges.Count - 1; i >= 0; i--) {
                var badge = badges[i];
                if (badge == null) { badges.RemoveAt(i); continue; } // cell destroyed with area
                badge.Refresh(show);
            }
        }

        static void Discover() {
            foreach (var cell in Object.FindObjectsOfType<PartyCharacterPCView>())
                EnsureBadge(cell);
            foreach (var cell in Object.FindObjectsOfType<PartyCharacterConsoleView>())
                EnsureBadge(cell);
        }

        static void EnsureBadge(ViewBase<PartyCharacterVM> cell) {
            if (cell.transform.Find("WT_PortraitToggle") != null) return;

            var (go, rect) = UIHelpers.Create("WT_PortraitToggle", cell.transform);
            rect.SetAnchor(0, 0, 1, 1); // point-anchor at the cell's top-left corner
            float size = 22f * UIHelpers.FontScale;
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = new Vector2(size * 0.5f + 2f, -(size * 0.5f + 2f));

            UIHelpers.AddBackground(go, new Color(0f, 0f, 0f, 0.65f));
            var label = UIHelpers.AddLabel(go, "T", 14f, TextAlignmentOptions.Midline, Color.white);
            label.outlineWidth = 0.25f;
            label.outlineColor = new Color32(0, 0, 0, 255);

            var badge = go.AddComponent<PortraitToggleBadge>();
            badge.Init(cell, label);
            go.AddComponent<Button>().onClick.AddListener(badge.OnClick);

            badges.Add(badge);
        }
    }
}
