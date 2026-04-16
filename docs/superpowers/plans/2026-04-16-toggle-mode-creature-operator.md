# ToggleMode & CreatureType Operator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow ToggleActivatable rules to explicitly turn abilities ON or OFF, and allow CreatureType conditions to use `!=` operator.

**Architecture:** Add a `ToggleMode` enum to the model layer, wire it through validator/executor, expose it as a dropdown in the action row UI (same pattern as HealMode/ThrowSplashMode). For CreatureType, replace the hardcoded `Equal` operator with a `=`/`!=` dropdown and respect the operator in the evaluator.

**Tech Stack:** C# / .NET 4.8.1 / Unity / Newtonsoft.Json

**Build command:** `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`

**Deploy command:** `./deploy.sh`

---

### Task 1: Model — ToggleMode enum and ActionDef property

**Files:**
- Modify: `WrathTactics/Models/Enums.cs:71` (after ThrowSplashMode)
- Modify: `WrathTactics/Models/TacticsRule.cs:34` (ActionDef class)

- [ ] **Step 1: Add ToggleMode enum**

In `WrathTactics/Models/Enums.cs`, add after the `ThrowSplashMode` enum (after line 71):

```csharp
public enum ToggleMode {
    On,
    Off
}
```

- [ ] **Step 2: Add ToggleMode property to ActionDef**

In `WrathTactics/Models/TacticsRule.cs`, add after line 34 (`SplashMode` property):

```csharp
[JsonProperty] public ToggleMode ToggleMode { get; set; } = ToggleMode.On;
```

Default `On` ensures existing saved rules with `ToggleActivatable` continue working.

- [ ] **Step 3: Build and verify**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Models/Enums.cs WrathTactics/Models/TacticsRule.cs
git commit -m "feat(model): add ToggleMode enum and ActionDef.ToggleMode property"
```

---

### Task 2: Engine — ActionValidator and CommandExecutor On/Off logic

**Files:**
- Modify: `WrathTactics/Engine/ActionValidator.cs:25-26` (CanExecute switch) and `:73-77` (CanToggleActivatable)
- Modify: `WrathTactics/Engine/CommandExecutor.cs:26-27` (Execute switch) and `:102-115` (ExecuteToggleActivatable)

- [ ] **Step 1: Pass ToggleMode through ActionValidator**

In `WrathTactics/Engine/ActionValidator.cs`, change the `ToggleActivatable` case in `CanExecute` (line 25-26) to pass the mode:

```csharp
case ActionType.ToggleActivatable:
    return CanToggleActivatable(action.AbilityId, owner, action.ToggleMode);
```

Then change the `CanToggleActivatable` method (lines 73-77) to:

```csharp
static bool CanToggleActivatable(string abilityGuid, UnitEntityData owner, ToggleMode mode) {
    var activatable = FindActivatable(owner, abilityGuid);
    if (activatable == null) return false;
    if (mode == ToggleMode.Off)
        return activatable.IsOn;
    return !activatable.IsOn && activatable.IsAvailable;
}
```

- [ ] **Step 2: Pass ToggleMode through CommandExecutor**

In `WrathTactics/Engine/CommandExecutor.cs`, change the `ToggleActivatable` case in `Execute` (line 26-27) to pass the mode:

```csharp
case ActionType.ToggleActivatable:
    return ExecuteToggleActivatable(action.AbilityId, owner, action.ToggleMode);
```

Then change `ExecuteToggleActivatable` (lines 102-115) to:

```csharp
static bool ExecuteToggleActivatable(string abilityGuid, UnitEntityData owner, ToggleMode mode) {
    var activatable = ActionValidator.FindActivatable(owner, abilityGuid);
    if (activatable == null) {
        Log.Engine.Warn($"Activatable {abilityGuid} not found on {owner.CharacterName}");
        return false;
    }

    if (mode == ToggleMode.Off) {
        activatable.IsOn = false;
        if (activatable.IsStarted)
            activatable.TryStop();
        Log.Engine.Info($"Toggled {activatable.Blueprint.name} OFF for {owner.CharacterName}");
    } else {
        activatable.IsOn = true;
        if (!activatable.IsStarted)
            activatable.TryStart();
        Log.Engine.Info($"Toggled {activatable.Blueprint.name} ON for {owner.CharacterName}");
    }
    return true;
}
```

- [ ] **Step 3: Build and verify**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/Engine/ActionValidator.cs WrathTactics/Engine/CommandExecutor.cs
git commit -m "feat(engine): support ToggleMode On/Off in validator and executor"
```

---

### Task 3: UI — ToggleMode dropdown in action row

**Files:**
- Modify: `WrathTactics/UI/RuleEditorWidget.cs:360-410` (SetupSpellSelector) and `:412-438` (RefreshSpellSelector)

The ToggleMode dropdown sits between the ActionType dropdown and the ability picker. Unlike Heal/ThrowSplash (which replace the ability picker with their mode selector), ToggleActivatable needs BOTH the mode dropdown AND the ability picker visible.

- [ ] **Step 1: Add ToggleMode dropdown in SetupSpellSelector**

In `WrathTactics/UI/RuleEditorWidget.cs`, in the `SetupSpellSelector` method, add a new block after the ThrowSplash block (after line 380) and before the `var entries = GetSpellEntries(...)` line (line 382):

```csharp
if (rule.Action.Type == ActionType.ToggleActivatable) {
    var toggleModeNames = Enum.GetNames(typeof(ToggleMode)).ToList();
    PopupSelector.Create(row, "ToggleMode", 0.39f, 0.52f, toggleModeNames,
        (int)rule.Action.ToggleMode, idx => {
            rule.Action.ToggleMode = (ToggleMode)idx;
            ConfigManager.Save();
        });

    var entries = GetSpellEntries(rule.Action.Type);
    currentSpellEntries = entries;
    var options = entries.Select(e => e.Name).ToList();
    var icons = entries.Select(e => e.Icon).ToList();
    int initialIndex = 0;
    if (!string.IsNullOrEmpty(rule.Action.AbilityId)) {
        int idx = entries.FindIndex(e => e.Guid == rule.Action.AbilityId);
        if (idx >= 0) initialIndex = idx;
    }
    if (entries.Count > 0 && string.IsNullOrEmpty(rule.Action.AbilityId)) {
        rule.Action.AbilityId = entries[initialIndex].Guid;
        ConfigManager.Save();
    }
    spellSelector = PopupSelector.CreateWithIcons(row, "SpellPick", 0.53f, 1.0f,
        options, icons, initialIndex, idx => {
            if (idx < currentSpellEntries.Count)
                rule.Action.AbilityId = currentSpellEntries[idx].Guid;
            ConfigManager.Save();
        });
    return;
}
```

This creates the mode dropdown at 0.39–0.52 and the ability picker at 0.53–1.0 (narrower than the default 0.39–1.0 to make room for the mode dropdown).

- [ ] **Step 2: Handle ToggleActivatable in RefreshSpellSelector**

In `RefreshSpellSelector` (around line 412), add ToggleActivatable to the types that need a full body rebuild (because it has a mode selector + ability picker):

Change the condition at line 414 from:

```csharp
if (actionType == ActionType.Heal || actionType == ActionType.ThrowSplash) {
```

to:

```csharp
if (actionType == ActionType.Heal || actionType == ActionType.ThrowSplash || actionType == ActionType.ToggleActivatable) {
```

This ensures switching TO ToggleActivatable rebuilds the body to show the mode dropdown.

- [ ] **Step 3: Build and verify**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/UI/RuleEditorWidget.cs
git commit -m "feat(ui): add ToggleMode On/Off dropdown for ToggleActivatable action"
```

---

### Task 4: CreatureType — `=`/`!=` operator support

**Files:**
- Modify: `WrathTactics/UI/ConditionRowWidget.cs:114-126` (count-subject branch) and `:177-204` (normal-subject branch)
- Modify: `WrathTactics/Engine/ConditionEvaluator.cs:244-245` and `:304-305` (CheckCreatureType call sites)

- [ ] **Step 1: Add operator dropdown for CreatureType in count-subject branch**

In `WrathTactics/UI/ConditionRowWidget.cs`, replace the CreatureType block in the count-subject branch (lines 114-126):

Old:
```csharp
} else if (condition.Property == ConditionProperty.CreatureType) {
    condition.Operator = ConditionOperator.Equal;
    var creatureTypes = new List<string> {
        "Aberration", "Animal", "Construct", "Dragon", "Fey",
        "Humanoid", "MagicalBeast", "MonstrousHumanoid", "Ooze",
        "Outsider", "Plant", "Swarm", "Undead", "Vermin"
    };
    int ctIdx = creatureTypes.IndexOf(condition.Value);
    if (ctIdx < 0) { ctIdx = 0; condition.Value = creatureTypes[0]; }
    PopupSelector.Create(root, "CountCreatureType", 0.58f, 0.88f, creatureTypes, ctIdx, v => {
        condition.Value = creatureTypes[v];
        ConfigManager.Save();
    });
```

New:
```csharp
} else if (condition.Property == ConditionProperty.CreatureType) {
    var ctOpNames = new List<string> { "=", "!=" };
    int ctOpIdx = condition.Operator == ConditionOperator.NotEqual ? 1 : 0;
    PopupSelector.Create(root, "CountCtOp", 0.58f, 0.64f, ctOpNames, ctOpIdx, v => {
        condition.Operator = v == 1 ? ConditionOperator.NotEqual : ConditionOperator.Equal;
        ConfigManager.Save();
    });
    var creatureTypes = new List<string> {
        "Aberration", "Animal", "Construct", "Dragon", "Fey",
        "Humanoid", "MagicalBeast", "MonstrousHumanoid", "Ooze",
        "Outsider", "Plant", "Swarm", "Undead", "Vermin"
    };
    int ctIdx = creatureTypes.IndexOf(condition.Value);
    if (ctIdx < 0) { ctIdx = 0; condition.Value = creatureTypes[0]; }
    PopupSelector.Create(root, "CountCreatureType", 0.65f, 0.88f, creatureTypes, ctIdx, v => {
        condition.Value = creatureTypes[v];
        ConfigManager.Save();
    });
```

- [ ] **Step 2: Add operator dropdown for CreatureType in normal-subject branch**

In the normal-subject branch (lines 177-204), CreatureType currently bypasses the operator dropdown. Change the `needsOperator` condition and the CreatureType rendering block.

Replace lines 177-204:

Old:
```csharp
bool isCreatureType = condition.Property == ConditionProperty.CreatureType;
bool isBuffProp = condition.Property == ConditionProperty.HasBuff
    || condition.Property == ConditionProperty.MissingBuff;
bool needsOperator = !isHasCondition && !isHasDebuff && !isCreatureType && !isBuffProp;

// Operator popup selector (hidden for dropdown-based properties)
if (needsOperator) {
    var opNames = new List<string> { "<", ">", "=", "!=", ">=", "<=" };
    PopupSelector.Create(root, "Operator", 0.38f, 0.50f, opNames,
        (int)condition.Operator, v => {
            condition.Operator = (ConditionOperator)v;
            ConfigManager.Save();
        });
} else {
    // Use Equal for dropdown matches
    condition.Operator = ConditionOperator.Equal;
}

if (isCreatureType) {
    var creatureTypes = new List<string> {
        "Aberration", "Animal", "Construct", "Dragon", "Fey",
        "Humanoid", "MagicalBeast", "MonstrousHumanoid", "Ooze",
        "Outsider", "Plant", "Swarm", "Undead", "Vermin"
    };
    int ctIdx = creatureTypes.IndexOf(condition.Value);
    if (ctIdx < 0) { ctIdx = 0; condition.Value = creatureTypes[0]; }
    PopupSelector.Create(root, "CreatureTypeValue", 0.38f, 0.88f, creatureTypes, ctIdx, v => {
        condition.Value = creatureTypes[v];
        ConfigManager.Save();
    });
```

New:
```csharp
bool isCreatureType = condition.Property == ConditionProperty.CreatureType;
bool isBuffProp = condition.Property == ConditionProperty.HasBuff
    || condition.Property == ConditionProperty.MissingBuff;
bool needsOperator = !isHasCondition && !isHasDebuff && !isBuffProp;

// Operator popup selector (hidden for dropdown-based properties)
if (needsOperator && !isCreatureType) {
    var opNames = new List<string> { "<", ">", "=", "!=", ">=", "<=" };
    PopupSelector.Create(root, "Operator", 0.38f, 0.50f, opNames,
        (int)condition.Operator, v => {
            condition.Operator = (ConditionOperator)v;
            ConfigManager.Save();
        });
} else if (isCreatureType) {
    var ctOpNames = new List<string> { "=", "!=" };
    int ctOpIdx = condition.Operator == ConditionOperator.NotEqual ? 1 : 0;
    PopupSelector.Create(root, "CtOperator", 0.38f, 0.44f, ctOpNames, ctOpIdx, v => {
        condition.Operator = v == 1 ? ConditionOperator.NotEqual : ConditionOperator.Equal;
        ConfigManager.Save();
    });
} else {
    condition.Operator = ConditionOperator.Equal;
}

if (isCreatureType) {
    var creatureTypes = new List<string> {
        "Aberration", "Animal", "Construct", "Dragon", "Fey",
        "Humanoid", "MagicalBeast", "MonstrousHumanoid", "Ooze",
        "Outsider", "Plant", "Swarm", "Undead", "Vermin"
    };
    int ctIdx = creatureTypes.IndexOf(condition.Value);
    if (ctIdx < 0) { ctIdx = 0; condition.Value = creatureTypes[0]; }
    PopupSelector.Create(root, "CreatureTypeValue", 0.45f, 0.88f, creatureTypes, ctIdx, v => {
        condition.Value = creatureTypes[v];
        ConfigManager.Save();
    });
```

- [ ] **Step 3: Respect operator in ConditionEvaluator**

In `WrathTactics/Engine/ConditionEvaluator.cs`, change the two `CheckCreatureType` call sites to respect the operator.

At line 244-245, change:
```csharp
case ConditionProperty.CreatureType:
    return CheckCreatureType(unit, condition.Value);
```
to:
```csharp
case ConditionProperty.CreatureType:
    bool ctMatch = CheckCreatureType(unit, condition.Value);
    return condition.Operator == ConditionOperator.NotEqual ? !ctMatch : ctMatch;
```

At line 304-305, apply the same change:
```csharp
case ConditionProperty.CreatureType:
    bool ctMatch2 = CheckCreatureType(unit, condition.Value);
    return condition.Operator == ConditionOperator.NotEqual ? !ctMatch2 : ctMatch2;
```

- [ ] **Step 4: Build and verify**

Run: `~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/UI/ConditionRowWidget.cs WrathTactics/Engine/ConditionEvaluator.cs
git commit -m "feat(engine+ui): support = and != operators for CreatureType conditions"
```

---

### Task 5: Deploy and manual test

- [ ] **Step 1: Deploy to Steam Deck**

Run: `./deploy.sh`

- [ ] **Step 2: Manual test checklist**

In-game on the Steam Deck:

1. Open Tactics panel (Ctrl+T)
2. Select Lann's tab
3. Create rule "Cold Iron ON":
   - Condition: Enemy → CreatureType **=** Outsider
   - Action: ToggleActivatable → **On** → Quiver of Cold Iron
   - Target: Self
4. Create rule "Cold Iron OFF" (lower priority):
   - Condition: Enemy → CreatureType **!=** Outsider
   - Action: ToggleActivatable → **Off** → Quiver of Cold Iron
   - Target: Self
5. Enter combat with mixed enemies (Outsiders + non-Outsiders)
6. Verify: quiver toggles ON when engaging Outsider, OFF when engaging non-Outsider
7. Verify: existing ToggleActivatable rules still work (default On mode)
