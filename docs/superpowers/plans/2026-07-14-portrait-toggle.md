# Per-Character Portrait Toggle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A small always-visible on/off badge on every party portrait that flips the existing per-character tactics switch `TacticsConfig.TacticsEnabled[unitId]`.

**Architecture:** New static `UI/PortraitToggleOverlay` synced from `Main.OnUpdate` attaches a `PortraitToggleBadge` MonoBehaviour to each pooled portrait cell (`PartyCharacterPCView` / `PartyCharacterConsoleView`, shared base `ViewBase<PartyCharacterVM>`). Badges never cache a unit — cells are re-bound on paging/party changes — and always derive display state from config (single source of truth). Spec: `docs/superpowers/specs/2026-07-14-portrait-toggle-design.md`.

**Tech Stack:** C# / .NET Framework 4.8.1, Unity UI + TextMeshPro, UnityModManager, game DLLs via `GamePath.props`.

## Global Constraints

- Build: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/` — the `-p:SolutionDir` flag is REQUIRED on Linux (without it DLL references break silently).
- Code style: K&R braces, 4-space indent, `var` when type is apparent.
- `catch (Exception)` is allowed ONLY as per-frame guard (used here in `Main.OnUpdate`), user-surface persistence, or static blueprint init.
- No new xUnit tests: the feature has no pure-logic surface (spec decision). Verification = clean build per task + mandatory Steam Deck smoke test (Task 4) BEFORE any release.
- Do NOT bump the version — `/release` owns the bump (target 1.23.0 happens there, after the smoke test).
- New i18n keys need entries in ALL FIVE locale files under `WrathTactics/Localization/` (en_GB at minimum, plus de/fr/ru/zh). Locale JSONs are EmbeddedResources — changes require rebuild + redeploy to test.
- IL-verified game facts (do not re-derive): portrait cells are pooled and re-bound via `PartyPCView.UpdateCharacterBindings()` on `PartyVM.StartIndex` (paging) and `PartyVM.GroupChanged`; `PartyCharacterVM` exposes public property `UnitEntityData` (backing `m_Unit`); no name-path exists to the party bar — discovery must be component-type based.

---

### Task 1: Config flag `ShowPortraitToggles`

**Files:**
- Modify: `WrathTactics/Models/TacticsConfig.cs` (class `TacticsConfig`, fields start ~line 5)

**Interfaces:**
- Produces: `TacticsConfig.ShowPortraitToggles` (`bool`, default `true`) — read by Tasks 2 and 3.

- [ ] **Step 1: Add the property**

Add below the existing `TacticsEnabled` property:

```csharp
        // Master switch for the per-portrait tactics badges (PortraitToggleOverlay).
        // Default true; missing field in legacy save configs deserializes to true
        // (bundled Newtonsoft ignores unknown/missing members — no migration).
        [JsonProperty] public bool ShowPortraitToggles { get; set; } = true;
```

- [ ] **Step 2: Build**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `0 Error(s)` (ignore `findstr` warnings — Linux artifact).

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Models/TacticsConfig.cs
git commit -m "feat(config): add ShowPortraitToggles flag (default on)"
```

---

### Task 2: Badge component + overlay sync + Main wiring

**Files:**
- Create: `WrathTactics/UI/PortraitToggleBadge.cs`
- Create: `WrathTactics/UI/PortraitToggleOverlay.cs`
- Modify: `WrathTactics/Main.cs` (`OnUpdate`, line ~43)
- Modify: `WrathTactics/UI/TacticsPanel.cs` (add one static method near `Instance`, line ~34)

**Interfaces:**
- Consumes: `TacticsConfig.ShowPortraitToggles` (Task 1); existing `ConfigManager.Current` / `ConfigManager.Save()` / `TacticsConfig.IsEnabled(string)`; `UIHelpers.Create/AddBackground/AddLabel/SetAnchor/FontScale`; game types `PartyCharacterPCView`, `PartyCharacterConsoleView`, `ViewBase<PartyCharacterVM>`, `PartyCharacterVM.UnitEntityData`.
- Produces: `PortraitToggleOverlay.Sync(float delta)` (static, called by `Main.OnUpdate`); `TacticsPanel.NotifyExternalConfigChange()` (static, called by badge clicks).

- [ ] **Step 1: Create `WrathTactics/UI/PortraitToggleBadge.cs`**

```csharp
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UI.MVVM._VM.Party;
using Owlcat.Runtime.UI.MVVM;
using TMPro;
using UnityEngine;
using WrathTactics.Persistence;

namespace WrathTactics.UI {
    // One badge per portrait cell. The cell is pooled and gets re-bound to a
    // different unit on paging/party changes, so the unit is re-read from the
    // cell's ViewModel on EVERY refresh and click — never cached.
    public class PortraitToggleBadge : MonoBehaviour {
        ViewBase<PartyCharacterVM> cell;
        TextMeshProUGUI label;

        static readonly Color OnColor = new Color(0.35f, 0.9f, 0.35f);

        public void Init(ViewBase<PartyCharacterVM> boundCell, TextMeshProUGUI stateLabel) {
            cell = boundCell;
            label = stateLabel;
        }

        UnitEntityData CurrentUnit() {
            if (cell == null) return null;           // Unity destroyed-equality
            return cell.ViewModel?.UnitEntityData;   // null on unbound pooled cells
        }

        public void Refresh(bool show) {
            var unit = show ? CurrentUnit() : null;
            if (unit == null) {
                if (gameObject.activeSelf) gameObject.SetActive(false);
                return;
            }
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            bool enabled = ConfigManager.Current.IsEnabled(unit.UniqueId);
            label.color = enabled ? OnColor : Color.gray;
            label.fontStyle = enabled ? FontStyles.Normal : FontStyles.Strikethrough;
        }

        public void OnClick() {
            var unit = CurrentUnit();
            if (unit == null) return;
            var config = ConfigManager.Current;
            config.TacticsEnabled[unit.UniqueId] = !config.IsEnabled(unit.UniqueId);
            ConfigManager.Save();
            TacticsPanel.NotifyExternalConfigChange();
            Refresh(config.ShowPortraitToggles);
        }
    }
}
```

- [ ] **Step 2: Create `WrathTactics/UI/PortraitToggleOverlay.cs`**

```csharp
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
```

Note: if `UIHelpers.AddLabel`'s actual signature differs (check `WrathTactics/UI/UIHelpers.cs:84`), match it — the call pattern above mirrors `TacticsPanel.CreateControlRow` (line ~246).

- [ ] **Step 3: Wire `Sync` into `Main.OnUpdate`**

In `WrathTactics/Main.cs`, inside `OnUpdate` after the existing `BuffPackScanner` try/catch block, add:

```csharp
            try {
                UI.PortraitToggleOverlay.Sync(delta);
            } catch (Exception ex) {
                Logging.Log.UI.Error(ex, "Portrait toggle sync error");
            }
```

(If `Log.UI` does not exist, use the same logger category the neighboring blocks use — check `WrathTactics/Logging/`.)

- [ ] **Step 4: Add the panel refresh hook**

In `WrathTactics/UI/TacticsPanel.cs`, directly below `public static TacticsPanel Instance => instance;` (line ~34), add:

```csharp
        // Lets external UI (portrait badges) refresh the open panel after
        // flipping TacticsEnabled, so the header toggle label never goes stale.
        public static void NotifyExternalConfigChange() {
            if (instance == null || !instance.gameObject.activeInHierarchy) return;
            instance.RefreshRuleList();
        }
```

- [ ] **Step 5: Build**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `0 Error(s)`. If `PartyCharacterConsoleView` fails to resolve, verify the namespace `Kingmaker.UI.MVVM._ConsoleView.Party` against the IL dump (`grep "_ConsoleView.Party.PartyCharacterConsoleView" <scratchpad>/Assembly-CSharp.il`).

- [ ] **Step 6: Commit**

```bash
git add WrathTactics/UI/PortraitToggleBadge.cs WrathTactics/UI/PortraitToggleOverlay.cs WrathTactics/Main.cs WrathTactics/UI/TacticsPanel.cs
git commit -m "feat(ui): per-character tactics toggle badges on party portraits"
```

---

### Task 3: UMM options checkbox + i18n

**Files:**
- Modify: `WrathTactics/Main.cs` (`Load`, line ~19 area, and new `OnGUI` method)
- Modify: `WrathTactics/Localization/en_GB.json`, `de_DE.json`, `fr_FR.json`, `ru_RU.json`, `zh_CN.json` (verify exact filenames with `ls WrathTactics/Localization/`)

**Interfaces:**
- Consumes: `TacticsConfig.ShowPortraitToggles` (Task 1), `ConfigManager.Current/Save()`, `.i18n()` extension (`WrathTactics/Localization/`).
- Produces: UMM mod-options checkbox (Ctrl+F10 → Wrath Tactics row) toggling the flag. NOTE: deviation from spec's "checkbox in the panel" — the panel has no free anchor slot (list 0.02–0.71, filter 0.72–0.76, controls 0.77–0.83); UMM options avoid shrinking the rule list. Deviation flagged to the user at plan time; if vetoed, shrink the rule list to 0.02–0.68 and place a SettingsRow at 0.69–0.71 instead.

- [ ] **Step 1: Register `OnGUI` in `Load`**

Next to `modEntry.OnUpdate = OnUpdate;` (line ~19) add:

```csharp
            modEntry.OnGUI = OnGUI;
```

- [ ] **Step 2: Implement `OnGUI`**

Add below `OnUpdate` in `Main.cs`:

```csharp
        static void OnGUI(UnityModManager.ModEntry modEntry) {
            if (Game.Instance?.Player == null) {
                GUILayout.Label("settings.load_save_first".i18n());
                return;
            }
            var config = Persistence.ConfigManager.Current;
            bool newVal = GUILayout.Toggle(config.ShowPortraitToggles,
                " " + "settings.portrait_toggles".i18n());
            if (newVal != config.ShowPortraitToggles) {
                config.ShowPortraitToggles = newVal;
                Persistence.ConfigManager.Save();
            }
        }
```

(Verify the `.i18n()` extension's namespace import matches other Main.cs usages; add the `using` if missing.)

- [ ] **Step 3: Add the two i18n keys to all five locale files**

Key/values (respect each file's JSON comma placement):

```json
"settings.portrait_toggles": "Show tactics on/off badges on the party portraits",
"settings.load_save_first": "Load a save to change Wrath Tactics settings."
```

- de_DE: `"Taktik-Schalter auf den Gruppenporträts anzeigen"` / `"Lade einen Spielstand, um Wrath-Tactics-Einstellungen zu ändern."`
- fr_FR: `"Afficher les badges tactiques sur les portraits du groupe"` / `"Chargez une sauvegarde pour modifier les réglages de Wrath Tactics."`
- ru_RU: `"Показывать переключатели тактики на портретах группы"` / `"Загрузите сохранение, чтобы изменить настройки Wrath Tactics."`
- zh_CN: `"在队伍头像上显示战术开关"` / `"请先读取存档再修改 Wrath Tactics 设置。"`

- [ ] **Step 4: Build**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `0 Error(s)`. (Locale JSONs are EmbeddedResources — a malformed JSON surfaces at runtime, not compile time; validate with `python3 -m json.tool WrathTactics/Localization/en_GB.json > /dev/null` for each edited file.)

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/Main.cs WrathTactics/Localization/
git commit -m "feat(ui): UMM options checkbox to hide portrait toggle badges"
```

---

### Task 4: Deck deployment + smoke test (gated on deck online)

**Files:** none (verification only). Requires `ssh deck-direct` reachable — if "no route to host", the deck may just be suspended: user wakes it, retry before declaring offline.

- [ ] **Step 1: Deploy**

Run: `./deploy.sh`
Expected: build + SCP of DLL/Info.json to the deck succeeds. If a prior build was skipped by mtime-miss, `touch` a modified .cs and rebuild.

- [ ] **Step 2: Smoke test in-game (user plays, checklist)**

1. Badges render on every party portrait; corner/size do not cover the level-up button or HP bar (tune `rect.anchoredPosition`/`size` if they do).
2. Click toggles green ↔ grey-strikethrough AND does NOT select/deselect the character (raycast interception works).
3. Disabled char: no rule fires for them (watch a fight); re-enable → rules resume.
4. State matches the panel header toggle: flip in panel, close panel → badge shows the new state. (Badge-side flips with the panel open are not testable — the panel's fullscreen backdrop blocks clicks on the badges; `NotifyExternalConfigChange` is defensive only.)
5. Party of >6: page with the arrows — badges show the correct per-unit state after paging (pooled-cell re-bind).
6. Console/gamepad UI mode (if active on deck): badges appear there too, or — if the console tree isn't live — no errors in the mod log.
7. UMM options (Ctrl+F10): unchecking hides all badges; re-checking restores them.
8. Mod log (`Mods/WrathTactics/Logs/`): no repeated "Portrait toggle sync error".

- [ ] **Step 3: Fix-forward or hand off to release**

Issues found → fix, rebuild, redeploy, re-test. All green → the user runs `/release` (minor → 1.23.0). Reply to grieva on Nexus AFTER the release is live, naming the version.
