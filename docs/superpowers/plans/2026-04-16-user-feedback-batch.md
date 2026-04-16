# User Feedback Batch Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add 8 stat-based targeting subjects, replace HasBuff/MissingBuff free-text with a searchable dropdown, and add vertical scrollbars to the rule editor.

**Architecture:** Three independent features that each touch different layers. Feature 1 adds enum values + evaluation/targeting logic. Feature 2 adds a new provider class and changes condition UI rendering. Feature 3 restructures the RuleEditorWidget layout hierarchy.

**Tech Stack:** C# / .NET 4.8.1, Unity UGUI, TextMeshPro, Kingmaker game API

---

## File Map

| File | Action | Responsibility |
|---|---|---|
| `WrathTactics/Models/Enums.cs` | Modify | Add 8 new `ConditionSubject` entries |
| `WrathTactics/Engine/ConditionEvaluator.cs` | Modify | Add metric functions + switch cases for new subjects |
| `WrathTactics/Engine/TargetResolver.cs` | Modify | Add target resolution for new subjects |
| `WrathTactics/UI/ConditionRowWidget.cs` | Modify | Wire new subjects into property lists; replace HasBuff/MissingBuff text inputs with PopupSelector |
| `WrathTactics/Engine/BuffBlueprintProvider.cs` | Create | Lazy-load all BlueprintBuff from game bundle, cache as (Name, Guid) list |
| `WrathTactics/UI/RuleEditorWidget.cs` | Modify | Wrap body in ScrollRect, cap max height |

---

## Task 1: Add stat-based targeting subjects to Enums

**Files:**
- Modify: `WrathTactics/Models/Enums.cs:2-13`

- [ ] **Step 1: Add 8 new ConditionSubject entries**

In `WrathTactics/Models/Enums.cs`, add after `EnemyLowestHp` (line 12):

```csharp
    public enum ConditionSubject {
        Self,
        Ally,
        AllyCount,
        Enemy,
        EnemyCount,
        Combat,
        EnemyBiggestThreat,  // the single enemy with highest threat
        EnemyLowestThreat,   // the single enemy with lowest threat
        EnemyHighestHp,      // the single enemy with highest HP%
        EnemyLowestHp,       // the single enemy with lowest HP%
        EnemyLowestAC,       // the single enemy with lowest AC
        EnemyHighestAC,      // the single enemy with highest AC
        EnemyLowestFort,     // the single enemy with lowest Fortitude save
        EnemyHighestFort,    // the single enemy with highest Fortitude save
        EnemyLowestReflex,   // the single enemy with lowest Reflex save
        EnemyHighestReflex,  // the single enemy with highest Reflex save
        EnemyLowestWill,     // the single enemy with lowest Will save
        EnemyHighestWill     // the single enemy with highest Will save
    }
```

- [ ] **Step 2: Build to verify enum compiles**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

Expected: Build succeeds (warnings about `findstr` are fine). The new enum values are unused so far — no errors.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Models/Enums.cs
git commit -m "feat: add 8 stat-based targeting subjects (AC, Fort, Reflex, Will)"
```

---

## Task 2: Add evaluation logic for new subjects

**Files:**
- Modify: `WrathTactics/Engine/ConditionEvaluator.cs:44-85`

- [ ] **Step 1: Add metric helper functions**

Add these static methods after the existing `HpPercent` method (line 85) in `ConditionEvaluator.cs`:

```csharp
        static float UnitAC(UnitEntityData unit) {
            return unit.Stats.AC.ModifiedValue;
        }

        static float UnitFort(UnitEntityData unit) {
            return unit.Stats.SaveFortitude.ModifiedValue;
        }

        static float UnitReflex(UnitEntityData unit) {
            return unit.Stats.SaveReflex.ModifiedValue;
        }

        static float UnitWill(UnitEntityData unit) {
            return unit.Stats.SaveWill.ModifiedValue;
        }
```

- [ ] **Step 2: Add switch cases in EvaluateCondition**

In the `EvaluateCondition` switch block (line 46-58), add cases before the `default`:

```csharp
                    case ConditionSubject.EnemyLowestAC:      return EvaluateEnemyPick(condition, owner, UnitAC, biggest: false);
                    case ConditionSubject.EnemyHighestAC:     return EvaluateEnemyPick(condition, owner, UnitAC, biggest: true);
                    case ConditionSubject.EnemyLowestFort:    return EvaluateEnemyPick(condition, owner, UnitFort, biggest: false);
                    case ConditionSubject.EnemyHighestFort:   return EvaluateEnemyPick(condition, owner, UnitFort, biggest: true);
                    case ConditionSubject.EnemyLowestReflex:  return EvaluateEnemyPick(condition, owner, UnitReflex, biggest: false);
                    case ConditionSubject.EnemyHighestReflex: return EvaluateEnemyPick(condition, owner, UnitReflex, biggest: true);
                    case ConditionSubject.EnemyLowestWill:    return EvaluateEnemyPick(condition, owner, UnitWill, biggest: false);
                    case ConditionSubject.EnemyHighestWill:   return EvaluateEnemyPick(condition, owner, UnitWill, biggest: true);
```

These reuse the existing `EvaluateEnemyPick` method which handles min/max selection via the `biggest` flag and sets `LastMatchedEnemy`.

- [ ] **Step 3: Build to verify**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Engine/ConditionEvaluator.cs
git commit -m "feat: evaluate conditions for stat-based enemy targeting subjects"
```

---

## Task 3: Add target resolution for new subjects

**Files:**
- Modify: `WrathTactics/Engine/TargetResolver.cs:9-24`
- Modify: `WrathTactics/UI/ConditionRowWidget.cs:284-323`

- [ ] **Step 1: Add target resolution helper methods**

In `TargetResolver.cs`, add after `GetEnemyHighestAC` (line 68):

```csharp
        static UnitEntityData GetEnemyLowestAC(UnitEntityData owner) {
            return GetVisibleEnemies(owner)
                .OrderBy(e => e.Stats.AC.ModifiedValue)
                .FirstOrDefault();
        }

        static UnitEntityData GetEnemyLowestFort(UnitEntityData owner) {
            return GetVisibleEnemies(owner)
                .OrderBy(e => e.Stats.SaveFortitude.ModifiedValue)
                .FirstOrDefault();
        }

        static UnitEntityData GetEnemyHighestFort(UnitEntityData owner) {
            return GetVisibleEnemies(owner)
                .OrderByDescending(e => e.Stats.SaveFortitude.ModifiedValue)
                .FirstOrDefault();
        }

        static UnitEntityData GetEnemyLowestReflex(UnitEntityData owner) {
            return GetVisibleEnemies(owner)
                .OrderBy(e => e.Stats.SaveReflex.ModifiedValue)
                .FirstOrDefault();
        }

        static UnitEntityData GetEnemyHighestReflex(UnitEntityData owner) {
            return GetVisibleEnemies(owner)
                .OrderByDescending(e => e.Stats.SaveReflex.ModifiedValue)
                .FirstOrDefault();
        }

        static UnitEntityData GetEnemyLowestWill(UnitEntityData owner) {
            return GetVisibleEnemies(owner)
                .OrderBy(e => e.Stats.SaveWill.ModifiedValue)
                .FirstOrDefault();
        }

        static UnitEntityData GetEnemyHighestWill(UnitEntityData owner) {
            return GetVisibleEnemies(owner)
                .OrderByDescending(e => e.Stats.SaveWill.ModifiedValue)
                .FirstOrDefault();
        }
```

- [ ] **Step 2: Add switch cases in Resolve**

In the `Resolve` switch block (line 10-23), add before `default`:

```csharp
                case TargetType.EnemyLowestAC:     return GetEnemyLowestAC(owner);
                case TargetType.EnemyHighestAC:    return GetEnemyHighestAC(owner);
                case TargetType.EnemyLowestFort:   return GetEnemyLowestFort(owner);
                case TargetType.EnemyHighestFort:  return GetEnemyHighestFort(owner);
                case TargetType.EnemyLowestReflex: return GetEnemyLowestReflex(owner);
                case TargetType.EnemyHighestReflex:return GetEnemyHighestReflex(owner);
                case TargetType.EnemyLowestWill:   return GetEnemyLowestWill(owner);
                case TargetType.EnemyHighestWill:  return GetEnemyHighestWill(owner);
```

Note: `TargetType` already has `EnemyHighestAC` (line 73 of Enums.cs). Add the remaining stat-based entries to `TargetType` in `Enums.cs` (after `EnemyHighestAC`):

```csharp
    public enum TargetType {
        Self,
        AllyLowestHp,
        AllyWithCondition,
        AllyMissingBuff,
        EnemyNearest,
        EnemyLowestHp,
        EnemyHighestHp,
        EnemyHighestAC,
        EnemyLowestAC,
        EnemyHighestFort,
        EnemyLowestFort,
        EnemyHighestReflex,
        EnemyLowestReflex,
        EnemyHighestWill,
        EnemyLowestWill,
        EnemyHighestThreat,
        EnemyCreatureType,
        ConditionTarget    // the enemy/ally that matched the triggering condition
    }
```

Then add the switch cases in `TargetResolver.Resolve` and the helper methods shown above.

- [ ] **Step 3: Wire new subjects into ConditionRowWidget property list**

In `ConditionRowWidget.cs`, update `GetPropertiesForSubject` (line 284-323). The new enemy-pick subjects need the same property list as the existing enemy subjects. Add them to the existing enemy case block:

```csharp
                case ConditionSubject.Enemy:
                case ConditionSubject.EnemyBiggestThreat:
                case ConditionSubject.EnemyLowestThreat:
                case ConditionSubject.EnemyHighestHp:
                case ConditionSubject.EnemyLowestHp:
                case ConditionSubject.EnemyLowestAC:
                case ConditionSubject.EnemyHighestAC:
                case ConditionSubject.EnemyLowestFort:
                case ConditionSubject.EnemyHighestFort:
                case ConditionSubject.EnemyLowestReflex:
                case ConditionSubject.EnemyHighestReflex:
                case ConditionSubject.EnemyLowestWill:
                case ConditionSubject.EnemyHighestWill:
                    return new List<ConditionProperty> {
                        ConditionProperty.HpPercent, ConditionProperty.AC,
                        ConditionProperty.SaveFortitude, ConditionProperty.SaveReflex, ConditionProperty.SaveWill,
                        ConditionProperty.HasBuff, ConditionProperty.HasCondition,
                        ConditionProperty.HasDebuff, ConditionProperty.CreatureType
                    };
```

- [ ] **Step 4: Build to verify**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/Models/Enums.cs WrathTactics/Engine/TargetResolver.cs WrathTactics/UI/ConditionRowWidget.cs
git commit -m "feat: add target resolution and UI wiring for stat-based subjects"
```

---

## Task 4: Create BuffBlueprintProvider

**Files:**
- Create: `WrathTactics/Engine/BuffBlueprintProvider.cs`

- [ ] **Step 1: Create the provider class**

Create `WrathTactics/Engine/BuffBlueprintProvider.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using WrathTactics.Logging;

namespace WrathTactics.Engine {
    public static class BuffBlueprintProvider {
        static List<BuffEntry> cachedBuffs;
        static bool isLoading;

        public struct BuffEntry {
            public string Name;
            public string Guid;
        }

        public static bool IsLoading => isLoading;
        public static bool IsLoaded => cachedBuffs != null;

        public static List<BuffEntry> GetBuffs() {
            if (cachedBuffs != null) return cachedBuffs;
            if (!isLoading) Load();
            return cachedBuffs ?? new List<BuffEntry>();
        }

        static void Load() {
            isLoading = true;
            try {
                var bundle = ResourcesLibrary.s_BlueprintsBundle;
                if (bundle == null) {
                    Log.Engine.Warn("BuffBlueprintProvider: BlueprintsBundle is null, cannot load buffs");
                    isLoading = false;
                    return;
                }

                var allBlueprints = bundle.LoadAllAssets<BlueprintBuff>();
                cachedBuffs = allBlueprints
                    .Where(b => b != null && !string.IsNullOrEmpty(b.name))
                    .Select(b => new BuffEntry {
                        Name = b.name,
                        Guid = b.AssetGuid.ToString()
                    })
                    .OrderBy(e => e.Name)
                    .ToList();

                Log.Engine.Info($"BuffBlueprintProvider: loaded {cachedBuffs.Count} buff blueprints");
            } catch (Exception ex) {
                Log.Engine.Error(ex, "BuffBlueprintProvider: failed to load buff blueprints");
                cachedBuffs = new List<BuffEntry>();
            } finally {
                isLoading = false;
            }
        }

        public static string GetName(string guid) {
            if (cachedBuffs == null || string.IsNullOrEmpty(guid)) return guid;
            foreach (var entry in cachedBuffs) {
                if (entry.Guid == guid) return entry.Name;
            }
            return guid;
        }
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

Expected: Build succeeds. The class compiles but is not yet wired into the UI.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Engine/BuffBlueprintProvider.cs
git commit -m "feat: add BuffBlueprintProvider for lazy-loading all game buff blueprints"
```

---

## Task 5: Replace HasBuff/MissingBuff free-text with PopupSelector

**Files:**
- Modify: `WrathTactics/UI/ConditionRowWidget.cs:162-170` (count layout) and `WrathTactics/UI/ConditionRowWidget.cs:254-262` (non-count layout)

The HasBuff/MissingBuff input appears in two places: the count-subject layout (line 162-170) and the non-count layout (line 254-262). Both currently fall through to a plain text input. Replace both with a PopupSelector backed by `BuffBlueprintProvider`.

- [ ] **Step 1: Add using directive**

Add to the top of `ConditionRowWidget.cs`:

```csharp
using WrathTactics.Engine;
```

- [ ] **Step 2: Extract a helper method for the buff dropdown**

Add this method to the `ConditionRowWidget` class, before `RefreshPropertySelector`:

```csharp
        void CreateBuffSelector(GameObject root, float xMin, float xMax) {
            bool isHasBuff = condition.Property == ConditionProperty.HasBuff
                || condition.Property == ConditionProperty.MissingBuff;
            if (!isHasBuff) return;

            if (BuffBlueprintProvider.IsLoading) {
                // Show loading label while buffs are being loaded
                var (loadLbl, loadLblRect) = UIHelpers.Create("BuffLoading", root.transform);
                loadLblRect.SetAnchor(xMin, xMax, 0, 1);
                loadLblRect.sizeDelta = Vector2.zero;
                UIHelpers.AddLabel(loadLbl, "Loading buffs...", 14f,
                    TextAlignmentOptions.MidlineLeft, new Color(0.7f, 0.7f, 0.5f));
                return;
            }

            var buffs = BuffBlueprintProvider.GetBuffs();
            if (buffs.Count == 0) {
                // Fallback to text input if no buffs loaded
                var valueInput = UIHelpers.CreateTMPInputField(root, "Value",
                    xMin, xMax, condition.Value ?? "", 16f);
                valueInput.onEndEdit.AddListener(v => {
                    condition.Value = v;
                    ConfigManager.Save();
                });
                return;
            }

            var names = buffs.Select(b => b.Name).ToList();
            int idx = buffs.FindIndex(b => b.Guid == condition.Value);
            if (idx < 0) { idx = 0; condition.Value = buffs[0].Guid; }

            PopupSelector.Create(root, "BuffValue", (float)xMin, (float)xMax, names, idx, v => {
                if (v < buffs.Count) condition.Value = buffs[v].Guid;
                ConfigManager.Save();
            });
        }
```

- [ ] **Step 3: Replace count-layout HasBuff/MissingBuff text input**

In the count-subject branch (around line 161-170), replace the `else` block that handles HasBuff/MissingBuff/IsDead:

Old code:
```csharp
                } else {
                    // HasBuff, MissingBuff, IsDead — fall back to text input
                    condition.Operator = ConditionOperator.Equal;
                    var valueInput = UIHelpers.CreateTMPInputField(root, "Value",
                        0.58, 0.88, condition.Value ?? "", 16f);
                    valueInput.onEndEdit.AddListener(v => {
                        condition.Value = v;
                        ConfigManager.Save();
                    });
                }
```

New code:
```csharp
                } else if (condition.Property == ConditionProperty.HasBuff
                    || condition.Property == ConditionProperty.MissingBuff) {
                    condition.Operator = ConditionOperator.Equal;
                    CreateBuffSelector(root, 0.58f, 0.88f);
                } else {
                    // IsDead — fall back to text input
                    condition.Operator = ConditionOperator.Equal;
                    var valueInput = UIHelpers.CreateTMPInputField(root, "Value",
                        0.58, 0.88, condition.Value ?? "", 16f);
                    valueInput.onEndEdit.AddListener(v => {
                        condition.Value = v;
                        ConfigManager.Save();
                    });
                }
```

- [ ] **Step 4: Replace non-count-layout HasBuff/MissingBuff text input**

In the non-count branch (around line 254-262), the `else` block currently renders a text input for all remaining properties including HasBuff/MissingBuff:

Old code:
```csharp
                } else {
                    // Normal single value input
                    var valueInput = UIHelpers.CreateTMPInputField(root, "Value",
                        0.51, 0.88, condition.Value ?? "", 16f);
                    valueInput.onEndEdit.AddListener(v => {
                        condition.Value = v;
                        ConfigManager.Save();
                    });
                }
```

New code:
```csharp
                } else if (condition.Property == ConditionProperty.HasBuff
                    || condition.Property == ConditionProperty.MissingBuff) {
                    CreateBuffSelector(root, 0.38f, 0.88f);
                } else {
                    // Normal single value input
                    var valueInput = UIHelpers.CreateTMPInputField(root, "Value",
                        0.51, 0.88, condition.Value ?? "", 16f);
                    valueInput.onEndEdit.AddListener(v => {
                        condition.Value = v;
                        ConfigManager.Save();
                    });
                }
```

Note: in the non-count layout, HasBuff/MissingBuff don't need an operator dropdown, so the buff selector starts at 0.38 (same position as other dropdown-based properties like CreatureType/HasCondition). The `needsOperator` check on line 173 should also exclude HasBuff/MissingBuff:

Update line 173 from:
```csharp
                bool needsOperator = !isHasCondition && !isHasDebuff && !isCreatureType;
```
to:
```csharp
                bool isBuffProp = condition.Property == ConditionProperty.HasBuff
                    || condition.Property == ConditionProperty.MissingBuff;
                bool needsOperator = !isHasCondition && !isHasDebuff && !isCreatureType && !isBuffProp;
```

- [ ] **Step 5: Build to verify**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add WrathTactics/UI/ConditionRowWidget.cs
git commit -m "feat: replace HasBuff/MissingBuff free-text with searchable buff dropdown"
```

---

## Task 6: Add vertical scrollbar to RuleEditorWidget

**Files:**
- Modify: `WrathTactics/UI/RuleEditorWidget.cs:42-67` (BuildUI) and `WrathTactics/UI/RuleEditorWidget.cs:576-605` (UpdateHeight)

- [ ] **Step 1: Wrap body in ScrollRect**

In `RuleEditorWidget.BuildUI()` (line 42-67), replace the body container setup with a scroll-enabled version. The key change: insert a ScrollRect + Viewport between root and body.

Replace lines 48-66 (from `// Body container` through the ContentSizeFitter):

Old code:
```csharp
            // Body container fills entire card — header is INSIDE the VLG
            var (body, bodyRt) = UIHelpers.Create("Body", root.transform);
            bodyContainer = body;
            bodyRt.FillParent();
            bodyRt.offsetMin = new Vector2(4, 4);
            bodyRt.offsetMax = new Vector2(-4, -4);

            var vlg = body.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.padding = new RectOffset(0, 0, 2, 2);

            var csf = body.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
```

New code:
```csharp
            // ScrollRect wrapper — clips body content when card exceeds max height
            var (scrollObj, scrollObjRect) = UIHelpers.Create("BodyScroll", root.transform);
            scrollObjRect.FillParent();
            scrollObjRect.offsetMin = new Vector2(4, 4);
            scrollObjRect.offsetMax = new Vector2(-4, -4);

            var (viewport, viewportRect) = UIHelpers.Create("Viewport", scrollObj.transform);
            viewportRect.FillParent();
            viewport.AddComponent<RectMask2D>();

            // Body container — content inside the scroll viewport
            var (body, bodyRt) = UIHelpers.Create("Body", viewport.transform);
            bodyContainer = body;
            bodyRt.SetAnchor(0, 1, 1, 1);
            bodyRt.pivot = new Vector2(0.5f, 1f);
            bodyRt.sizeDelta = new Vector2(0, 0);

            var vlg = body.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 4;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.padding = new RectOffset(0, 0, 2, 2);

            var csf = body.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollObj.AddComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = bodyRt;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;
```

- [ ] **Step 2: Cap max height in UpdateHeight**

In `UpdateHeight` (line 576-605), cap the final height at 500px. Change the last line of the non-linked branch:

Old code (line 604):
```csharp
            layoutElement.preferredHeight = Mathf.Max(160f, height);
```

New code:
```csharp
            layoutElement.preferredHeight = Mathf.Clamp(height, 160f, 500f);
```

- [ ] **Step 3: Build to verify**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/UI/RuleEditorWidget.cs
git commit -m "feat: add vertical scrollbar to rule editor, cap card height at 500px"
```

---

## Task 7: Deploy and verify on Steam Deck

**Files:** None (deployment only)

- [ ] **Step 1: Build and deploy**

Run: `./deploy.sh`

This builds the mod and deploys DLL + Info.json to the Steam Deck.

- [ ] **Step 2: Verify Feature 1 — stat-based targeting subjects**

In-game:
- Open Tactics panel (Ctrl+T)
- Create a new rule
- Click the Subject dropdown — verify all 8 new entries appear: `EnemyLowestAC`, `EnemyHighestAC`, `EnemyLowestFort`, `EnemyHighestFort`, `EnemyLowestReflex`, `EnemyHighestReflex`, `EnemyLowestWill`, `EnemyHighestWill`
- Select `EnemyLowestWill` as subject
- Verify the Property dropdown shows enemy-appropriate properties (HpPercent, AC, saves, HasBuff, etc.)
- Set an action (e.g. CastSpell with a Will-save spell)
- Enter combat and verify the rule targets the enemy with lowest Will save

- [ ] **Step 3: Verify Feature 2 — buff dropdown**

In-game:
- Create a rule with Subject = Enemy, Property = HasBuff
- Verify a dropdown appears (not a text field) with buff names
- If many buffs loaded, verify scrolling works in the popup
- Select a buff and verify the rule saves correctly
- Verify MissingBuff also shows the dropdown
- Check the mod log for the "loaded X buff blueprints" message

- [ ] **Step 4: Verify Feature 3 — scrollbar**

In-game:
- Create a rule and add 10+ conditions using "+ Condition"
- Verify the card stops growing at ~500px height
- Verify vertical scrollbar appears and scrolling works inside the card
- Verify the outer rule list still scrolls between rules independently

- [ ] **Step 5: Commit version bump if all green**

```bash
# Update version in Info.json if needed
git add -A
git commit -m "chore: verify deployment of user feedback batch features"
```
