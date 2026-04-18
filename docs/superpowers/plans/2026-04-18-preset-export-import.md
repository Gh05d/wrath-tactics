# Preset Export/Import and Default Presets Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship six default presets, a rule→preset promote button, clipboard export (single-rule or full-collection, uniform array format), and a paste-based import modal for presets.

**Architecture:** Additive — no model/schema changes. `PresetRegistry` gains `SeedDefaults()` (called at end of `Reload()`) and `PromoteRuleToPreset(rule, config)`. `RuleEditorWidget` header gets two new buttons (`Export`, `↥ Preset`). `PresetPanel` gets three buttons (`Export All`, `Import`, `Open Folder`). New `ImportPresetOverlay` renders a modal with multiline TMP input + inline error handling. Seeding is idempotent via fixed IDs; user deletions stick.

**Tech Stack:** C# / .NET 4.8.1 / Unity UGUI / TextMeshPro / Newtonsoft.Json / Kingmaker game API

**Build command:** `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

**Deploy command:** `./deploy.sh`

**Spec:** `docs/superpowers/specs/2026-04-18-preset-export-import-design.md`

**Testing model:** No unit-test harness in this repo. Every phase ends with a build; the penultimate phase is a manual Steam Deck smoke test.

---

## File Map

| File | Action | Responsibility |
|---|---|---|
| `WrathTactics/Engine/DefaultPresets.cs` | Create | Static factory returning six `TacticsRule` defaults with fixed IDs |
| `WrathTactics/Engine/PresetRegistry.cs` | Modify | Add `SeedDefaults()` called from `Reload()`; add `PromoteRuleToPreset(rule, config)` |
| `WrathTactics/UI/ImportPresetOverlay.cs` | Create | Modal overlay with multiline TMP input, validate-and-apply logic |
| `WrathTactics/UI/PresetPanel.cs` | Modify | Add `Export All` / `Import` / `Open Folder` buttons |
| `WrathTactics/UI/RuleEditorWidget.cs` | Modify | Add `Export` button (always) and `↥ Preset` button (gated on `unitId != null && !isLinked`) |
| `WrathTactics/Info.json` | Modify | Version bump |
| `WrathTactics/WrathTactics.csproj` | Modify | Version bump |

---

## Phase 1: Default Presets

### Task 1: Create `DefaultPresets` factory

**Files:**
- Create: `WrathTactics/Engine/DefaultPresets.cs`

- [ ] **Step 1: Write the file**

Write to `WrathTactics/Engine/DefaultPresets.cs`:

```csharp
using System.Collections.Generic;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>
    /// Seeded once per fresh install via PresetRegistry.SeedDefaults. Uses fixed IDs so
    /// "file exists?" check is idempotent across mod reloads and version bumps. User
    /// deletions and manual edits are never overwritten.
    /// </summary>
    public static class DefaultPresets {
        public static List<TacticsRule> Build() {
            return new List<TacticsRule> {
                EmergencySelfHeal(),
                PartyChannelHeal(),
                CounterSwarms(),
                CoupDeGrace(),
                ChannelVsUndead(),
                SmiteEvil(),
            };
        }

        static TacticsRule EmergencySelfHeal() => new TacticsRule {
            Id = "default-emergency-self-heal",
            Name = "Emergency Self-Heal",
            Enabled = true,
            Priority = 0,
            CooldownRounds = 1,
            ConditionGroups = new List<ConditionGroup> {
                new ConditionGroup { Conditions = new List<Condition> {
                    new Condition {
                        Subject = ConditionSubject.Self,
                        Property = ConditionProperty.HpPercent,
                        Operator = ConditionOperator.LessThan,
                        Value = "30",
                    },
                }},
            },
            Action = new ActionDef { Type = ActionType.Heal, HealMode = HealMode.Any },
            Target = new TargetDef { Type = TargetType.Self },
        };

        static TacticsRule PartyChannelHeal() => new TacticsRule {
            Id = "default-party-channel-heal",
            Name = "Party Heal (Channel Positive)",
            Enabled = true,
            Priority = 0,
            CooldownRounds = 1,
            ConditionGroups = new List<ConditionGroup> {
                new ConditionGroup { Conditions = new List<Condition> {
                    new Condition {
                        Subject = ConditionSubject.AllyCount,
                        Property = ConditionProperty.HpPercent,
                        Operator = ConditionOperator.LessThan,
                        Value = "60",
                        Value2 = "2",
                    },
                }},
            },
            Action = new ActionDef {
                Type = ActionType.CastAbility,
                AbilityId = "f5fc9a1a2a3c1a946a31b320d1dd31b2",  // Cleric ChannelEnergy (heal)
            },
            Target = new TargetDef { Type = TargetType.Self },
        };

        static TacticsRule CounterSwarms() => new TacticsRule {
            Id = "default-counter-swarms",
            Name = "Counter Swarms (Splash)",
            Enabled = true,
            Priority = 0,
            CooldownRounds = 1,
            ConditionGroups = new List<ConditionGroup> {
                new ConditionGroup { Conditions = new List<Condition> {
                    new Condition {
                        Subject = ConditionSubject.Enemy,
                        Property = ConditionProperty.CreatureType,
                        Operator = ConditionOperator.Equal,
                        Value = "Swarm",
                    },
                }},
            },
            Action = new ActionDef { Type = ActionType.ThrowSplash, SplashMode = ThrowSplashMode.Strongest },
            Target = new TargetDef { Type = TargetType.EnemyNearest },
        };

        static TacticsRule CoupDeGrace() => new TacticsRule {
            Id = "default-coup-de-grace",
            Name = "Coup de Grace on Helpless",
            Enabled = true,
            Priority = 0,
            CooldownRounds = 1,
            ConditionGroups = new List<ConditionGroup> {
                new ConditionGroup { Conditions = new List<Condition> {
                    new Condition {
                        Subject = ConditionSubject.Enemy,
                        Property = ConditionProperty.HasCondition,
                        Operator = ConditionOperator.Equal,
                        Value = "Sleeping",
                    },
                }},
                new ConditionGroup { Conditions = new List<Condition> {
                    new Condition {
                        Subject = ConditionSubject.Enemy,
                        Property = ConditionProperty.HasCondition,
                        Operator = ConditionOperator.Equal,
                        Value = "Paralyzed",
                    },
                }},
            },
            Action = new ActionDef { Type = ActionType.AttackTarget },
            Target = new TargetDef { Type = TargetType.ConditionTarget },
        };

        static TacticsRule ChannelVsUndead() => new TacticsRule {
            Id = "default-channel-vs-undead",
            Name = "Channel Against Undead",
            Enabled = true,
            Priority = 0,
            CooldownRounds = 1,
            ConditionGroups = new List<ConditionGroup> {
                new ConditionGroup { Conditions = new List<Condition> {
                    new Condition {
                        Subject = ConditionSubject.Enemy,
                        Property = ConditionProperty.CreatureType,
                        Operator = ConditionOperator.Equal,
                        Value = "Undead",
                    },
                }},
            },
            Action = new ActionDef {
                Type = ActionType.CastAbility,
                AbilityId = "279447a6bf2d3544d93a0a39c3b8e91d",  // Cleric ChannelPositiveHarm
            },
            Target = new TargetDef { Type = TargetType.Self },
        };

        static TacticsRule SmiteEvil() => new TacticsRule {
            Id = "default-smite-evil",
            Name = "Smite Evil",
            Enabled = true,
            Priority = 0,
            CooldownRounds = 1,
            ConditionGroups = new List<ConditionGroup> {
                new ConditionGroup { Conditions = new List<Condition> {
                    new Condition {
                        Subject = ConditionSubject.Enemy,
                        Property = ConditionProperty.Alignment,
                        Operator = ConditionOperator.Equal,
                        Value = "Evil",
                    },
                }},
            },
            Action = new ActionDef {
                Type = ActionType.CastAbility,
                AbilityId = "7bb9eb2042e67bf489ccd1374423cdec",  // Paladin SmiteEvilAbility
            },
            Target = new TargetDef { Type = TargetType.EnemyHighestThreat },
        };
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Engine/DefaultPresets.cs
git commit -m "feat(engine): add DefaultPresets factory with six seed rules"
```

---

### Task 2: Seed defaults in `PresetRegistry.Reload`

**Files:**
- Modify: `WrathTactics/Engine/PresetRegistry.cs`

- [ ] **Step 1: Add `SeedDefaults` method and call it from `Reload`**

In `WrathTactics/Engine/PresetRegistry.cs`, find the `Reload` method:

```csharp
        public static void Reload() {
            presets = PresetManager.LoadAll().ToDictionary(r => r.Id, r => r);
            Log.Persistence.Info($"PresetRegistry loaded {presets.Count} presets");
        }
```

Replace with:

```csharp
        public static void Reload() {
            presets = PresetManager.LoadAll().ToDictionary(r => r.Id, r => r);
            Log.Persistence.Info($"PresetRegistry loaded {presets.Count} presets");
            SeedDefaults();
        }

        /// <summary>
        /// Writes any DefaultPresets whose ID is not already present on disk. Idempotent:
        /// user-deleted or user-edited defaults are left alone because the check is
        /// per-ID, and SeedDefaults only runs on fresh installs for missing IDs.
        /// </summary>
        static void SeedDefaults() {
            int seeded = 0;
            foreach (var preset in DefaultPresets.Build()) {
                if (presets.ContainsKey(preset.Id)) continue;
                PresetManager.Save(preset);
                presets[preset.Id] = preset;
                seeded++;
            }
            if (seeded > 0) Log.Persistence.Info($"Seeded {seeded} default preset(s)");
        }
```

- [ ] **Step 2: Build to verify**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Engine/PresetRegistry.cs
git commit -m "feat(engine): seed DefaultPresets on PresetRegistry.Reload"
```

---

## Phase 2: Promote rule → preset

### Task 3: Add `PromoteRuleToPreset` helper

**Files:**
- Modify: `WrathTactics/Engine/PresetRegistry.cs`

- [ ] **Step 1: Add the helper method at the bottom of the class (before the closing brace)**

Append the following method just before the closing `}` of the `PresetRegistry` class:

```csharp
        /// <summary>
        /// Promotes a standalone rule into a new preset and links the original rule to it.
        /// The original rule's body (ConditionGroups / Action / Target) is cleared — all
        /// future reads go through Resolve() to the preset. Caller must ConfigManager.Save
        /// and rebuild the affected UI after this returns.
        /// </summary>
        public static TacticsRule PromoteRuleToPreset(TacticsRule rule) {
            if (rule == null) return null;
            if (!string.IsNullOrEmpty(rule.PresetId)) {
                Log.Persistence.Warn("PromoteRuleToPreset called on an already-linked rule — ignored");
                return Get(rule.PresetId);
            }
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(rule);
            var preset = Newtonsoft.Json.JsonConvert.DeserializeObject<TacticsRule>(json);
            preset.Id = System.Guid.NewGuid().ToString();
            preset.PresetId = null;
            Save(preset);

            rule.PresetId = preset.Id;
            rule.ConditionGroups = new System.Collections.Generic.List<Models.ConditionGroup>();
            rule.Action = new Models.ActionDef();
            rule.Target = new Models.TargetDef();
            return preset;
        }
```

- [ ] **Step 2: Build to verify**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Engine/PresetRegistry.cs
git commit -m "feat(engine): add PromoteRuleToPreset helper"
```

---

### Task 4: Add `↥ Preset` button to rule header

**Files:**
- Modify: `WrathTactics/UI/RuleEditorWidget.cs`

- [ ] **Step 1: Insert the button between `Copy` and `Delete`**

In `WrathTactics/UI/RuleEditorWidget.cs`, find the Copy button creation (around line 246-253). Immediately **after** that block (after `copyObj.AddComponent<Button>().onClick.AddListener(() => CloneRule());`) and **before** the `// Delete` comment, insert:

```csharp
            // Promote to preset — only for unlinked character rules
            bool canPromote = !isLinked && !string.IsNullOrEmpty(unitId);
            if (canPromote) {
                var (promoteObj, _4p) = UIHelpers.Create("Promote", header.transform);
                var promoteLE = promoteObj.AddComponent<LayoutElement>();
                promoteLE.preferredWidth = 64;
                promoteLE.flexibleWidth = 0;
                UIHelpers.AddBackground(promoteObj, new Color(0.25f, 0.45f, 0.3f, 1f));
                UIHelpers.AddLabel(promoteObj, "↥ Preset", 13f, TextAlignmentOptions.Midline);
                promoteObj.AddComponent<Button>().onClick.AddListener(() => PromoteToPreset());
            }
```

- [ ] **Step 2: Add the `PromoteToPreset` method next to `CloneRule`**

Still in `RuleEditorWidget.cs`, find the `CloneRule()` method (around line 674). Add the following method immediately after the closing `}` of `CloneRule` and before the closing `}` of the class:

```csharp
        void PromoteToPreset() {
            var preset = Engine.PresetRegistry.PromoteRuleToPreset(rule);
            if (preset == null) return;
            ConfigManager.Save();
            onChanged?.Invoke();
        }
```

- [ ] **Step 3: Build**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/UI/RuleEditorWidget.cs
git commit -m "feat(ui): add ↥ Preset button to promote character rules into presets"
```

---

## Phase 3: Export (clipboard)

### Task 5: Add per-rule `Export` button

**Files:**
- Modify: `WrathTactics/UI/RuleEditorWidget.cs`

- [ ] **Step 1: Insert the button between `Copy` and `↥ Preset`**

In `RuleEditorWidget.cs`, immediately **after** the Copy block and **before** the `// Promote to preset` block added in Task 4, insert:

```csharp
            // Export (clipboard) — wraps the resolved rule in a 1-element JSON array
            var (exportObj, _4e) = UIHelpers.Create("Export", header.transform);
            var exportLE = exportObj.AddComponent<LayoutElement>();
            exportLE.preferredWidth = 56;
            exportLE.flexibleWidth = 0;
            UIHelpers.AddBackground(exportObj, new Color(0.3f, 0.3f, 0.5f, 1f));
            UIHelpers.AddLabel(exportObj, "Export", 13f, TextAlignmentOptions.Midline);
            exportObj.AddComponent<Button>().onClick.AddListener(() => ExportRuleToClipboard());
```

- [ ] **Step 2: Add the `ExportRuleToClipboard` method**

Still in `RuleEditorWidget.cs`, add this method next to `PromoteToPreset`:

```csharp
        void ExportRuleToClipboard() {
            var source = Engine.PresetRegistry.Resolve(rule);
            var array = new System.Collections.Generic.List<TacticsRule> { source };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(array, Newtonsoft.Json.Formatting.Indented);
            UnityEngine.GUIUtility.systemCopyBuffer = json;
            Logging.Log.UI.Info($"Copied rule '{source.Name}' to clipboard");
        }
```

- [ ] **Step 3: Build**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/UI/RuleEditorWidget.cs
git commit -m "feat(ui): add per-rule Export button (clipboard, array format)"
```

---

### Task 6: Add `Export All Presets` button

**Files:**
- Modify: `WrathTactics/UI/PresetPanel.cs`

- [ ] **Step 1: Insert an export-all button in the header**

In `WrathTactics/UI/PresetPanel.cs`, find the `// New preset button` block (around line 48-63). Immediately **before** the `// New preset button` comment, insert:

```csharp
            // Export All Presets — copies the whole collection as a JSON array
            var (exportAllBtn, _ea) = UIHelpers.Create("ExportAllBtn", root.transform);
            exportAllBtn.AddComponent<LayoutElement>().preferredHeight = 36;
            UIHelpers.AddBackground(exportAllBtn, new Color(0.3f, 0.3f, 0.5f, 1f));
            UIHelpers.AddLabel(exportAllBtn, "Export All Presets to Clipboard", 15f, TextAlignmentOptions.Midline);
            exportAllBtn.AddComponent<Button>().onClick.AddListener(() => {
                var all = new System.Collections.Generic.List<TacticsRule>(PresetRegistry.All());
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(all, Newtonsoft.Json.Formatting.Indented);
                UnityEngine.GUIUtility.systemCopyBuffer = json;
                Log.UI.Info($"Copied {all.Count} preset(s) to clipboard");
            });
```

Note: `Log.UI` is already imported at the top of `PresetPanel.cs` via `using WrathTactics.Logging;`. If not, add the using.

- [ ] **Step 2: Build**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/UI/PresetPanel.cs
git commit -m "feat(ui): add Export All Presets button to PresetPanel"
```

---

## Phase 4: Import modal

### Task 7: Create `ImportPresetOverlay`

**Files:**
- Create: `WrathTactics/UI/ImportPresetOverlay.cs`

- [ ] **Step 1: Write the overlay file**

Write to `WrathTactics/UI/ImportPresetOverlay.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using WrathTactics.Engine;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.UI {
    /// <summary>
    /// Full-screen modal that lets the user paste a JSON array of TacticsRule. On confirm,
    /// each rule is saved as a new preset with a fresh Guid and a (imported) suffix on name
    /// collision. Inline validation; modal stays open on parse failure so the user can fix.
    /// </summary>
    public class ImportPresetOverlay : MonoBehaviour {
        Action onImported;
        TMP_InputField input;
        TextMeshProUGUI errorLabel;

        public static ImportPresetOverlay Show(Transform canvasParent, Action onImported) {
            var (obj, rect) = UIHelpers.Create("ImportPresetOverlay", canvasParent);
            rect.FillParent();
            var overlay = obj.AddComponent<ImportPresetOverlay>();
            overlay.onImported = onImported;
            overlay.BuildUI();
            return overlay;
        }

        void BuildUI() {
            UIHelpers.AddBackground(gameObject, new Color(0, 0, 0, 0.6f));

            // Click-outside-to-close backdrop
            var closeBtn = gameObject.AddComponent<Button>();
            closeBtn.onClick.AddListener(Close);

            // Centered panel
            var (panel, panelRect) = UIHelpers.Create("Panel", transform);
            panelRect.SetAnchor(0.2, 0.8, 0.15, 0.85);
            panelRect.sizeDelta = Vector2.zero;
            UIHelpers.AddBackground(panel, new Color(0.12f, 0.12f, 0.14f, 0.98f));
            // Block click-through on the panel
            panel.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.14f, 0.98f);

            var vlg = panel.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 8;
            vlg.padding = new RectOffset(14, 14, 14, 14);
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;

            // Title
            var (title, _t) = UIHelpers.Create("Title", panel.transform);
            title.AddComponent<LayoutElement>().preferredHeight = 32;
            UIHelpers.AddLabel(title, "Import Presets", 20f,
                TextAlignmentOptions.MidlineLeft, Color.white);

            // Hint
            var (hint, _h) = UIHelpers.Create("Hint", panel.transform);
            hint.AddComponent<LayoutElement>().preferredHeight = 32;
            UIHelpers.AddLabel(hint,
                "Paste a JSON array of rules (from another user's Export). Duplicates get a \"(imported)\" suffix.",
                13f, TextAlignmentOptions.TopLeft, new Color(0.7f, 0.7f, 0.7f));

            // Multiline input (large, fills most of the panel)
            input = UIHelpers.CreateTMPInputField(panel, "Json", 0, 1, "", 13f);
            input.lineType = TMP_InputField.LineType.MultiLineNewline;
            var inputLE = input.gameObject.AddComponent<LayoutElement>();
            inputLE.flexibleHeight = 1;
            inputLE.preferredHeight = 300;

            // Error label (empty by default)
            var (errorObj, _err) = UIHelpers.Create("Error", panel.transform);
            errorObj.AddComponent<LayoutElement>().preferredHeight = 40;
            errorLabel = UIHelpers.AddLabel(errorObj, "", 13f,
                TextAlignmentOptions.MidlineLeft, new Color(1f, 0.5f, 0.4f));

            // Buttons row
            var (buttons, _btn) = UIHelpers.Create("Buttons", panel.transform);
            buttons.AddComponent<LayoutElement>().preferredHeight = 40;
            var hlg = buttons.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;

            var (cancelBtn, _c) = UIHelpers.Create("CancelBtn", buttons.transform);
            UIHelpers.AddBackground(cancelBtn, new Color(0.35f, 0.35f, 0.35f));
            UIHelpers.AddLabel(cancelBtn, "Cancel", 15f, TextAlignmentOptions.Midline);
            cancelBtn.AddComponent<Button>().onClick.AddListener(Close);

            var (importBtn, _i) = UIHelpers.Create("ImportBtn", buttons.transform);
            UIHelpers.AddBackground(importBtn, new Color(0.2f, 0.45f, 0.2f));
            UIHelpers.AddLabel(importBtn, "Import", 15f, TextAlignmentOptions.Midline);
            importBtn.AddComponent<Button>().onClick.AddListener(OnImportClicked);
        }

        void OnImportClicked() {
            var text = input.text?.Trim() ?? "";
            if (string.IsNullOrEmpty(text)) {
                errorLabel.text = "Paste a JSON array first.";
                return;
            }

            List<TacticsRule> parsed;
            try {
                parsed = JsonConvert.DeserializeObject<List<TacticsRule>>(text);
            } catch (JsonException ex) {
                errorLabel.text = $"Invalid JSON: {ex.Message}";
                return;
            }
            if (parsed == null) {
                errorLabel.text = "Expected a JSON array.";
                return;
            }

            var existingNames = new HashSet<string>(
                PresetRegistry.All().Select(p => p.Name),
                StringComparer.OrdinalIgnoreCase);

            int imported = 0, renamed = 0;
            foreach (var preset in parsed) {
                if (preset == null) continue;
                preset.Id = Guid.NewGuid().ToString();
                preset.PresetId = null;
                string baseName = string.IsNullOrEmpty(preset.Name) ? "Imported Preset" : preset.Name;
                string finalName = baseName;
                if (existingNames.Contains(finalName)) {
                    int n = 1;
                    finalName = $"{baseName} (imported)";
                    while (existingNames.Contains(finalName)) {
                        n++;
                        finalName = $"{baseName} (imported {n})";
                    }
                    renamed++;
                }
                preset.Name = finalName;
                existingNames.Add(finalName);
                PresetRegistry.Save(preset);
                imported++;
            }

            Log.UI.Info($"Imported {imported} preset(s) ({renamed} renamed due to name conflicts)");
            onImported?.Invoke();
            Close();
        }

        void Close() {
            Destroy(gameObject);
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/UI/ImportPresetOverlay.cs
git commit -m "feat(ui): add ImportPresetOverlay modal for paste-based preset import"
```

---

### Task 8: Wire `Import` button in `PresetPanel`

**Files:**
- Modify: `WrathTactics/UI/PresetPanel.cs`

- [ ] **Step 1: Insert an `Import` button right after the `Export All` button added in Task 6**

In `PresetPanel.cs`, immediately **after** the `exportAllBtn.AddComponent<Button>().onClick.AddListener(...)` block from Task 6 and **before** the `// New preset button` comment, insert:

```csharp
            // Import Presets — opens the ImportPresetOverlay modal
            var (importBtn, _imp) = UIHelpers.Create("ImportBtn", root.transform);
            importBtn.AddComponent<LayoutElement>().preferredHeight = 36;
            UIHelpers.AddBackground(importBtn, new Color(0.2f, 0.45f, 0.3f, 1f));
            UIHelpers.AddLabel(importBtn, "Import Presets from Clipboard Paste", 15f, TextAlignmentOptions.Midline);
            importBtn.AddComponent<Button>().onClick.AddListener(() => {
                // Parent the modal on the top canvas so it overlays everything else
                var canvas = UnityEngine.GameObject.FindObjectOfType<Canvas>()?.transform ?? root.transform.root;
                ImportPresetOverlay.Show(canvas, () => {
                    onPresetsChanged?.Invoke();
                    Rebuild();
                });
            });
```

- [ ] **Step 2: Build**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/UI/PresetPanel.cs
git commit -m "feat(ui): wire Import Presets button to ImportPresetOverlay"
```

---

## Phase 5: Open folder

### Task 9: Add `Open Presets Folder` button at the bottom

**Files:**
- Modify: `WrathTactics/UI/PresetPanel.cs`

- [ ] **Step 1: Insert an open-folder button at the end of `BuildUI`**

In `PresetPanel.cs`, the `BuildUI` method currently ends with:

```csharp
            foreach (var preset in presets) {
                CreatePresetEntry(root.transform, preset);
            }
        }
```

Insert the open-folder button inside `BuildUI` but **before** the final closing brace of the method — after the `foreach` loop (so it renders below the preset entries):

```csharp
            foreach (var preset in presets) {
                CreatePresetEntry(root.transform, preset);
            }

            // Open Presets folder (manual file-based sharing / backup)
            var (folderBtn, _folder) = UIHelpers.Create("FolderBtn", root.transform);
            folderBtn.AddComponent<LayoutElement>().preferredHeight = 32;
            UIHelpers.AddBackground(folderBtn, new Color(0.25f, 0.25f, 0.3f, 1f));
            UIHelpers.AddLabel(folderBtn, "Open Presets Folder", 14f, TextAlignmentOptions.Midline);
            folderBtn.AddComponent<Button>().onClick.AddListener(() => {
                var dir = System.IO.Path.Combine(Main.ModPath, "Presets");
                try {
                    System.IO.Directory.CreateDirectory(dir);
                    Application.OpenURL("file://" + dir);
                } catch (Exception ex) {
                    Log.UI.Warn($"Could not open presets folder '{dir}': {ex.Message}");
                }
            });
        }
```

Important: the existing early-return branch for empty presets (`if (presets.Count == 0) { ... return; }`) will not render the folder button in the empty state. That is acceptable — the user only needs the folder button if they have presets to back up or share. If you want the button in the empty case too, duplicate the same insert block just before that `return`.

- [ ] **Step 2: Build**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/UI/PresetPanel.cs
git commit -m "feat(ui): add Open Presets Folder button for manual backup"
```

---

## Phase 6: Deploy + manual verification

### Task 10: Deploy and smoke-test

**Files:** none modified.

- [ ] **Step 1: Release build + deploy**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -c Release -p:SolutionDir=$(pwd)/
./deploy.sh
```

Expected: deploy completes without SSH errors.

- [ ] **Step 2: Smoke-test on the Deck**

Launch the game, load a save. Run through each scenario from the spec:

1. **Promote path.** On a character tab, build a rule with 2 condition groups, an action, and a target. Click `↥ Preset`. Open the Presets tab — new preset appears with matching content. Back on character tab — original row shows as linked (badge, preset-data summary). Rename the preset — character tab label updates.
2. **Export rule.** Click `Export` on a rule. Open a text editor or terminal, paste from clipboard — must be a JSON array with a single element in valid format.
3. **Export all presets.** On the Presets tab, click `Export All Presets to Clipboard`. Paste into a text editor — valid JSON array containing every preset.
4. **Import happy path.** Copy an exported array into clipboard. Presets tab → `Import Presets from Clipboard Paste`. Paste into the modal, click `Import`. Modal closes, presets appear in the list. Repeat the paste/import — new entries appear with ` (imported)` suffix.
5. **Import invalid JSON.** Open Import modal. Paste `not json`, click Import. Inline error shows "Invalid JSON: …". Modal stays open. Paste `{}` — error "Expected a JSON array."
6. **Import empty.** Click Import on empty field — inline error "Paste a JSON array first."
7. **Default seeding, fresh install.** SSH to the Deck, delete the entire `{ModPath}/Presets/` directory, reload the mod (via UMM in-game or by restarting). Open the Presets tab — six default presets present.
8. **Default deletion persists.** Delete `Emergency Self-Heal` via the panel's Delete button. Reload the mod. Preset stays gone; the other five remain.
9. **Default manual edit persists.** Rename `Smite Evil` → `My Smite`, reload mod — `My Smite` preserved.
10. **Non-cleric + Channel default.** On a pure Fighter, link the `Channel Against Undead` default via `+ From Preset`. Spawn an undead encounter. Verify no crash; rule sits in the list but doesn't cast (validator skips).
11. **Open folder.** Click `Open Presets Folder` on Deck — Dolphin (or the default file manager) opens at `{ModPath}/Presets/`.

- [ ] **Step 3: If all eleven pass, proceed to release. If any fail, fix before tagging.**

---

## Phase 7: Release

### Task 11: Version bump

**Files:**
- Modify: `WrathTactics/Info.json`
- Modify: `WrathTactics/WrathTactics.csproj`

- [ ] **Step 1: Pick version**

Current version is `0.5.0` (from the last release). This change adds user-visible features without breaking schema — bump to `0.6.0`. Both files must match.

- [ ] **Step 2: Edit both files**

In `WrathTactics/Info.json`, change `"Version": "0.5.0"` to `"Version": "0.6.0"`.
In `WrathTactics/WrathTactics.csproj`, change `<Version>0.5.0</Version>` to `<Version>0.6.0</Version>`.

- [ ] **Step 3: Release build**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -c Release -p:SolutionDir=$(pwd)/
```

Expected: `WrathTactics/bin/WrathTactics-0.6.0.zip` is produced.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Info.json WrathTactics/WrathTactics.csproj
git commit -m "chore: release 0.6.0 — preset export/import and defaults"
```

- [ ] **Step 5: Hand back to user for tagging + push.**

Print a short summary and leave `git tag` / `git push` to the user.

---

## Notes for the implementer

- **Build warnings about `findstr`** on Linux are harmless (Windows-only shell invocation in csproj auto-detection target).
- **`Log` namespaces:** use `Log.UI` for UI code, `Log.Engine` for engine code, `Log.Persistence` for the seeding helper — matches existing patterns in `PresetRegistry`, `PresetPanel`, and `RuleEditorWidget`.
- **Anchor numbers** in the import modal panel (`0.2-0.8, 0.15-0.85`) are reasonable defaults; adjust slightly if the modal looks cramped on the Deck's 1280×800 display.
- **Name-conflict case-insensitivity:** the `HashSet<string>` uses `StringComparer.OrdinalIgnoreCase` as required by the spec.
- **`Resolve` semantics:** in `ExportRuleToClipboard` we resolve through the preset, so exporting a linked rule writes the preset's content (not a dangling link). This is correct — sharing a link to a preset the recipient doesn't have is useless.
