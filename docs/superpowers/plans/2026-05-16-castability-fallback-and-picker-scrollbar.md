# CastAbility Fallback Chain + Picker Scrollbar Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unlock the existing `FallbackAbilityIds` chain for `ActionType.CastAbility` (Dragon Breath variants, Bloodline elemental alternatives, Mythic abilities) and add a visible vertical `Scrollbar` to the shared popup picker overlay so the "From preset" picker and every other popup dropdown expose scrollability.

**Architecture:** Pure dispatch + UI gate change. No new model, no new enums, no migration. `CommandExecutor` and `ActionValidator` route `CastAbility` through the chain-aware `ResolveCastSpellChain` instead of the GUID-only short paths; the GUID-only `ExecuteCastSpell(string, ...)` overload becomes dead code and is removed. `RuleEditorWidget.Action.cs` and `RuleEditorWidget.cs` broaden their `CastSpell`-only gates to include `CastAbility`. `UIHelpers.CreatePickerOverlay` adds a vertical Scrollbar wired to the existing ScrollRect, with `AutoHideAndExpandViewport` so short popups stay uncluttered.

**Tech Stack:** C# / .NET Framework 4.8.1, Unity UI (uGUI), Harmony patches, Newtonsoft.Json (bundled), Pathfinder: WotR game DLLs.

**Verification strategy:** The mod's `WrathTactics.Tests/` suite covers pure logic only (no `Game.Instance`, no Unity); engine and UI paths are not unit-tested. Verification is **manual smoke testing on Steam Deck** after each behavior-changing commit, per the spec's Test Plan section. Build success after each task is the unit verification.

---

## File Map

**Modify:**
- `WrathTactics/Engine/CommandExecutor.cs` — Task 1 — `CastAbility` dispatch + remove dead GUID overload
- `WrathTactics/Engine/ActionValidator.cs` — Task 2 — `CastAbility` both branches (Point + Unit) through chain-aware resolver
- `WrathTactics/UI/RuleEditorWidget.Action.cs` — Task 3 — gate + picker-entries type
- `WrathTactics/UI/RuleEditorWidget.cs` — Task 3 — fallback-row height calc
- `WrathTactics/UI/UIHelpers.cs` — Task 4 — Scrollbar wiring in `CreatePickerOverlay`
- `WrathTactics/Models/TacticsRule.cs` — Task 5 — `FallbackAbilityIds` comment update
- `CLAUDE.md` — Task 5 — Gotchas entries

**No new files.**

---

## Task 1: Route CastAbility through chain-aware executor

**Files:**
- Modify: `WrathTactics/Engine/CommandExecutor.cs:19-23` (dispatch switch), `:134-163` (delete dead overload)

- [ ] **Step 1: Edit the dispatch switch**

Open `WrathTactics/Engine/CommandExecutor.cs`. Replace lines 19-23 (the `CastSpell` + `CastAbility` cases) with a fall-through that routes both to the chain-aware `ExecuteCastSpell(ActionDef, ...)` overload.

Before:
```csharp
                    case ActionType.CastSpell:
                        return ExecuteCastSpell(action, owner, target);
                    case ActionType.CastAbility:
                        return ExecuteCastSpell(action.AbilityId, owner, target);
```

After:
```csharp
                    case ActionType.CastSpell:
                    case ActionType.CastAbility:
                        return ExecuteCastSpell(action, owner, target);
```

- [ ] **Step 2: Delete the now-dead GUID-only overload**

In the same file, delete the entire `ExecuteCastSpell(string abilityGuid, UnitEntityData owner, ResolvedTarget target)` method body (lines 134-163, including the surrounding blank lines). The dispatch change in Step 1 was its only caller — confirmed at design time via `grep -n "ExecuteCastSpell" WrathTactics/Engine/CommandExecutor.cs` (only the line-23 call site).

The method to delete starts with this signature line:
```csharp
        static bool ExecuteCastSpell(string abilityGuid, UnitEntityData owner, ResolvedTarget target) {
```
and ends with the closing brace before `ExecuteUseItem` begins.

- [ ] **Step 3: Verify build succeeds**

Run:
```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded` with `0 Error(s)`. Warnings about `findstr` are harmless (Windows-only auto-detection target). If the compiler reports any unresolved reference to `ExecuteCastSpell(string, ...)`, a call site was missed — grep again and route it through the `ActionDef` overload too.

- [ ] **Step 4: Run the pure-logic test suite**

Run:
```bash
~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/
```

Expected: all tests pass. These tests cover `ConditionEvaluator.CompareCount`, `BuffBlueprintProvider`, `CommonBuffRegistry`, `RangeBrackets` — none touch `CommandExecutor`, so the result is unchanged. Build break here means the test project pulled in our edit transitively (unlikely; mod assembly is loaded via `InternalsVisibleTo`) — investigate.

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/Engine/CommandExecutor.cs
git commit -m "$(cat <<'EOF'
feat(engine): route CastAbility through chain-aware executor

CastAbility dispatch now hits ExecuteCastSpell(ActionDef, ...) like
CastSpell does, so FallbackAbilityIds entries become reachable for
class abilities (Dragon Breath variants, Bloodline powers, etc.).
The GUID-only ExecuteCastSpell(string, ...) overload had no other
callers and is removed.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Route CastAbility through chain-aware validator

**Files:**
- Modify: `WrathTactics/Engine/ActionValidator.cs:25-26` (Point branch), `:41-42` (Unit branch)

- [ ] **Step 1: Update the Point-target branch**

Open `WrathTactics/Engine/ActionValidator.cs`. In the `if (target.IsPoint)` block, replace the `CastAbility` case (line 25-26) so it shares the `CastSpell` resolution path. The branch must also re-check `CanTargetPoint` on the resolved ability (mirrors the existing `CastSpell` Point branch).

Before:
```csharp
                    case ActionType.CastAbility:
                        return CanCastAbilityAtPoint(action.AbilityId, owner);
```

After:
```csharp
                    case ActionType.CastAbility: {
                        ItemEntity _unused;
                        string _unusedId;
                        var ability = ResolveCastSpellChain(owner, target, action, out _unused, out _unusedId);
                        if (ability == null) return false;
                        if (!ability.CanTargetPoint) {
                            Log.Engine.Trace($"CanCastAbilityAtPoint: {owner.CharacterName} ability '{ability.Name}' is not point-castable");
                            return false;
                        }
                        return true;
                    }
```

- [ ] **Step 2: Update the Unit-target branch**

In the same file, replace the `CastAbility` case at line 41-42 to dispatch through the chain-aware resolver.

Before:
```csharp
                case ActionType.CastAbility:
                    return CanCastSpell(action.AbilityId, owner, unit);
```

After:
```csharp
                case ActionType.CastAbility: {
                    ItemEntity _unused;
                    string _unusedId;
                    return ResolveCastSpellChain(owner, target, action, out _unused, out _unusedId) != null;
                }
```

- [ ] **Step 3: Verify build succeeds**

Run:
```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded` with `0 Error(s)`.

If the compiler warns that `CanCastAbilityAtPoint` or `CanCastSpell` are now unused (CS0414 / IDE0051), leave them — they're partial-class members that other call sites (e.g. `ActionValidator.Find.cs` heal pickers) may still reference. Only remove if `grep -rn "CanCastAbilityAtPoint\|CanCastSpell" WrathTactics/` shows no remaining call sites. (Quick check at plan-write time: `CanCastSpell` is called from heal-picker code — keep it.)

- [ ] **Step 4: Run the pure-logic test suite**

Run:
```bash
~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/Engine/ActionValidator.cs
git commit -m "$(cat <<'EOF'
feat(engine): validate CastAbility through chain-aware resolver

ActionValidator.CanExecute now resolves CastAbility via
ResolveCastSpellChain in both Point and Unit branches, so the
validator and executor stay in lock-step: a CastAbility rule is
"valid right now" if its primary OR any FallbackAbilityIds entry
is currently castable. Without this change the executor would
queue a fallback cast that the validator just rejected, breaking
rule fall-through.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Unlock the fallback UI for CastAbility

**Files:**
- Modify: `WrathTactics/UI/RuleEditorWidget.Action.cs:241` (gate), `:262` (picker entries)
- Modify: `WrathTactics/UI/RuleEditorWidget.cs:234-237` (height calc)

- [ ] **Step 1: Broaden the `SetupFallbackRows` gate**

Open `WrathTactics/UI/RuleEditorWidget.Action.cs`. At line 241, replace the single-type gate so both Cast types render fallback rows.

Before:
```csharp
        void SetupFallbackRows(Transform parent) {
            if (rule.Action.Type != ActionType.CastSpell) return;
```

After:
```csharp
        void SetupFallbackRows(Transform parent) {
            if (rule.Action.Type != ActionType.CastSpell
                && rule.Action.Type != ActionType.CastAbility) return;
```

- [ ] **Step 2: Make the picker entries type-aware**

In the same file at line 262, replace the hardcoded `ActionType.CastSpell` with the rule's current Action.Type so the picker shows abilities for CastAbility rules and spells for CastSpell rules.

Before:
```csharp
        void BuildFallbackRow(Transform parent, int index) {
            var entries = GetSpellEntries(ActionType.CastSpell);
```

After:
```csharp
        void BuildFallbackRow(Transform parent, int index) {
            var entries = GetSpellEntries(rule.Action.Type);
```

- [ ] **Step 3: Update the fallback-row height calc**

Open `WrathTactics/UI/RuleEditorWidget.cs`. At lines 234-237, broaden the two `CastSpell`-only checks to also include `CastAbility`.

Before:
```csharp
            int fallbackCount = rule.Action.Type == ActionType.CastSpell
                ? (rule.Action.FallbackAbilityIds?.Count ?? 0)
                : 0;
            bool showAddFallback = rule.Action.Type == ActionType.CastSpell;
```

After:
```csharp
            bool chainCapable = rule.Action.Type == ActionType.CastSpell
                             || rule.Action.Type == ActionType.CastAbility;
            int fallbackCount = chainCapable
                ? (rule.Action.FallbackAbilityIds?.Count ?? 0)
                : 0;
            bool showAddFallback = chainCapable;
```

- [ ] **Step 4: Verify build succeeds**

Run:
```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded` with `0 Error(s)`.

- [ ] **Step 5: Run the pure-logic test suite**

Run:
```bash
~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add WrathTactics/UI/RuleEditorWidget.Action.cs WrathTactics/UI/RuleEditorWidget.cs
git commit -m "$(cat <<'EOF'
feat(ui): show fallback rows for CastAbility rules

SetupFallbackRows + the height calculator now treat CastSpell and
CastAbility identically. BuildFallbackRow's picker entries follow
rule.Action.Type so a CastAbility rule's fallback picker lists
class abilities (Dragon Breath variants) rather than spellbook
spells.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 4: Add visible Scrollbar to CreatePickerOverlay

**Files:**
- Modify: `WrathTactics/UI/UIHelpers.cs:436-528` (CreatePickerOverlay body)

- [ ] **Step 1: Insert the Scrollbar wiring**

Open `WrathTactics/UI/UIHelpers.cs`. Locate `CreatePickerOverlay` (begins at line 436). The current `ScrollRect` setup ends around lines 487-492:

```csharp
            var scroll = scrollObj.AddComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;
```

Replace that block with the version below, which adds a vertical Scrollbar GameObject as a sibling of the viewport, configures its `Handle Rect`, wires it onto the ScrollRect, and uses `AutoHideAndExpandViewport` so short popups don't reserve the 12px gutter.

After:
```csharp
            var scroll = scrollObj.AddComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = contentRect;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;

            // Visible vertical scrollbar — 12px wide, anchored to popup's right edge.
            // AutoHideAndExpandViewport leaves short popups uncluttered (bar hidden +
            // viewport reclaims the 12px) and only shows the bar when content overflows.
            float sbWidth = 12f * UIHelpers.FontScale;
            var (sbObj, sbRect) = UIHelpers.Create("Scrollbar", scrollObj.transform);
            sbRect.anchorMin = new Vector2(1, 0);
            sbRect.anchorMax = new Vector2(1, 1);
            sbRect.pivot = new Vector2(1, 0.5f);
            sbRect.sizeDelta = new Vector2(sbWidth, 0);
            sbRect.anchoredPosition = Vector2.zero;
            UIHelpers.AddBackground(sbObj, new Color(0.10f, 0.10f, 0.10f, 0.7f));

            var (slidingArea, slidingRect) = UIHelpers.Create("SlidingArea", sbObj.transform);
            slidingRect.anchorMin = new Vector2(0, 0);
            slidingRect.anchorMax = new Vector2(1, 1);
            slidingRect.sizeDelta = new Vector2(-4, -4);
            slidingRect.anchoredPosition = Vector2.zero;

            var (handleObj, handleRect) = UIHelpers.Create("Handle", slidingArea.transform);
            handleRect.anchorMin = new Vector2(0, 0);
            handleRect.anchorMax = new Vector2(1, 1);
            handleRect.sizeDelta = Vector2.zero;
            var handleImg = handleObj.AddComponent<Image>();
            handleImg.color = new Color(0.45f, 0.45f, 0.45f, 1f);
            handleImg.raycastTarget = true;

            var sb = sbObj.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;
            sb.handleRect = handleRect;
            sb.targetGraphic = handleImg;

            scroll.verticalScrollbar = sb;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scroll.verticalScrollbarSpacing = 0f;
```

- [ ] **Step 2: Verify build succeeds**

Run:
```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded` with `0 Error(s)`. If `Scrollbar` or `Image` are not in scope, the existing `using UnityEngine.UI;` at the top of `UIHelpers.cs` should cover both — confirm the using is present. If not, add it.

- [ ] **Step 3: Run the pure-logic test suite**

Run:
```bash
~/.dotnet/dotnet test WrathTactics.Tests/WrathTactics.Tests.csproj -p:SolutionDir=$(pwd)/
```

Expected: all tests pass.

- [ ] **Step 4: Commit**

```bash
git add WrathTactics/UI/UIHelpers.cs
git commit -m "$(cat <<'EOF'
feat(ui): visible vertical scrollbar in popup picker overlay

CreatePickerOverlay now mounts a 12px Scrollbar at the right edge of
the popup, wired to the existing ScrollRect with AutoHideAndExpandViewport
so short popups stay uncluttered. Affects every popup dropdown in the
mod (From-preset picker, condition Property/Subject/Operator pickers,
Class picker, etc.) — fixes the discoverability gap on Steam Deck where
mouse-wheel scrolling worked but no visual cue indicated more content
below.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 5: Documentation updates

**Files:**
- Modify: `WrathTactics/Models/TacticsRule.cs:37-40` (XML-style comment)
- Modify: `CLAUDE.md` (root) — append two Gotchas entries

- [ ] **Step 1: Update the FallbackAbilityIds comment**

Open `WrathTactics/Models/TacticsRule.cs`. Replace the three-line comment + property at lines 37-41 to reflect the broadened applicability.

Before:
```csharp
        // CastSpell fallback chain: tried in order after AbilityId when the primary resolver misses
        // (no slot, no scroll, UMD fail, etc.). Each entry goes through the full Sources mask,
        // so a fallback can still fall through Spellbook -> Wand -> Scroll -> Potion for itself.
        // Empty on legacy rules; only consulted by ActionType.CastSpell.
        [JsonProperty] public List<string> FallbackAbilityIds { get; set; } = new();
```

After:
```csharp
        // CastSpell / CastAbility fallback chain: tried in order after AbilityId when the
        // primary resolver misses (no slot, no scroll, UMD fail, resource exhausted, etc.).
        // Each entry goes through the full Sources mask, so a fallback can still fall through
        // Spellbook -> Wand -> Scroll -> Potion for itself (CastSpell only; class abilities
        // only match the Spell branch because their GUIDs don't appear in inventory items).
        // Type-homogeneous — entries share the parent rule's Action.Type. Empty on legacy rules.
        [JsonProperty] public List<string> FallbackAbilityIds { get; set; } = new();
```

- [ ] **Step 2: Append the new Gotcha entry to CLAUDE.md**

Open `CLAUDE.md` at the repo root. Locate the "## Gotchas" section (search for `## Gotchas`). Append the following bullet at the **end** of the existing list inside that section, immediately before the next `##` heading:

```markdown
- **Cast fallback chain spans CastSpell + CastAbility (since 1.15)**: `ActionDef.FallbackAbilityIds` is consulted by `ResolveCastSpellChain` for BOTH `ActionType.CastSpell` and `ActionType.CastAbility`. The chain is type-homogeneous: entries share the rule's `Action.Type` because the picker pulls entries from `GetSpellEntries(rule.Action.Type)`. Switching `Action.Type` between Cast types does NOT clear stale GUIDs — entries remain in the list but the resolver misses them (different blueprint pool). `ResolveCastSpellChain` is the authoritative validator + executor entry point for both Cast types; never re-introduce `CanCastSpell(action.AbilityId, ...)` / `CanCastAbilityAtPoint(action.AbilityId, ...)` on the CastAbility branches — that bypasses the chain and breaks rule fall-through.
```

- [ ] **Step 3: Strengthen the Validator-Strictness Gotcha**

In the same `CLAUDE.md`, locate the existing entry that starts with `**Validator strictness is load-bearing**` (search `Validator strictness`). The entry currently reads:

```markdown
- **Validator strictness is load-bearing**: New `ActionType` MUST validate up-front in `ActionValidator.CanExecute`, including `AbilityData.IsAvailable`. Casts queue then silently drop, blocking rule fall-through. ([deep-dive](docs/wrath-api-deep-dive.md#validator-strictness))
```

Replace it with:

```markdown
- **Validator strictness is load-bearing**: New `ActionType` MUST validate up-front in `ActionValidator.CanExecute`, including `AbilityData.IsAvailable`. Casts queue then silently drop, blocking rule fall-through. **For chain-capable action types (CastSpell, CastAbility), the validator MUST route through `ResolveCastSpellChain` — single-GUID validation misses the chain, so a fallback-only rule (primary unavailable) is incorrectly rejected and the next rule fires instead. Validator and executor must agree on which GUID will be cast.** ([deep-dive](docs/wrath-api-deep-dive.md#validator-strictness))
```

- [ ] **Step 4: Verify build succeeds (sanity — only docs touched)**

Run:
```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded`. (Comment-only edits should never break the build, but rerunning catches accidental code edits.)

- [ ] **Step 5: Commit**

```bash
git add WrathTactics/Models/TacticsRule.cs CLAUDE.md
git commit -m "$(cat <<'EOF'
docs: fallback chain applies to CastSpell + CastAbility

Inline comment on FallbackAbilityIds and two CLAUDE.md Gotchas
entries documenting that the chain spans both Cast types since
1.15, that ResolveCastSpellChain is authoritative for validator
and executor on both, and that the chain is type-homogeneous.

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>
EOF
)"
```

---

## Task 6: Deploy and manual smoke test on Steam Deck

**Files:** None (deploy + run).

This task verifies the runtime behavior the unit tests can't cover. Per the project's "No CI by design" stance (see `WrathTactics/CLAUDE.md`), this is the authoritative correctness check before release.

- [ ] **Step 1: Deploy the Debug build to Steam Deck**

Run:
```bash
./deploy.sh
```

Expected output ends with a successful `scp` of `WrathTactics.dll` and `Info.json` to `/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/WrathTactics/`. If `deploy.sh` fails the deck connectivity check, see "Steam Deck Deployment" in `wrath-mods/CLAUDE.md`.

- [ ] **Step 2: Verify the deployed DLL is fresh**

Run:
```bash
ssh deck-direct "stat -c '%y %n' '/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/WrathTactics/WrathTactics.dll'"
```

Expected: timestamp within the last few minutes. If older, the deploy didn't land — re-run `./deploy.sh` and check stderr.

- [ ] **Step 3: Smoke test — Spell fallback regression**

Launch the game on the deck. Load a save with an existing CastSpell rule that has a fallback configured (one of the default presets that uses fallback is fine — open Tactics panel, Presets tab, find a preset using `FallbackAbilityIds`). Verify:
- The `+ Fallback` button still appears on the rule.
- Existing fallback entries render with the correct ability name + icon.
- Triggering combat fires the rule and the executor still falls back correctly when the primary is unavailable. Watch the latest log file (path below) for `CastSpell fallback hit for X: primary=... -> used=...`.

Log path:
```
/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/WrathTactics/Logs/wrath-tactics-YYYY-MM-DD-HHMMSS.log
```

Tail latest:
```bash
ssh deck-direct "ls -t '/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/WrathTactics/Logs/' | head -1"
```

- [ ] **Step 4: Smoke test — CastAbility fallback happy path**

In the same save (or load a Dragon-Bloodline / Mythic-Dragon character), create a new rule:
- Action: `CastAbility`
- Primary: a Dragon Breath form (e.g. Cone of Fire variant)
- Click `+ Fallback` — verify the button appears (didn't appear pre-change).
- Add a fallback entry — verify the picker shows class abilities (not spellbook spells).
- Pick a second breath form (e.g. Cone of Cold variant).

Trigger combat with the primary on cooldown (cast it once, wait for it to enter cooldown, then queue the rule again). Verify:
- Rule fires the fallback.
- Log line `CastSpell fallback hit for <Character>: primary=<guid1> -> used=<guid2>`.

- [ ] **Step 5: Smoke test — CastAbility without fallback unchanged**

Create a CastAbility rule with no fallback entries (default state). Trigger it. Verify:
- Primary ability casts normally.
- Log lines identical to pre-change behavior (no `fallback hit` noise).

- [ ] **Step 6: Smoke test — Type-switch behavior**

Take a CastSpell rule with 1-2 fallbacks. In-game, switch its Action type to CastAbility via the dropdown. Verify:
- Existing fallback GUIDs remain in the list (no auto-clear, as documented).
- Picker for those rows now shows class abilities; the stale spell GUIDs no longer resolve.
- Trigger the rule: log warns `ResolveCastSpellChain returned null for X, primary=..., chain=N` rather than crashing.

This is the documented "accept" behavior per spec — confirms graceful degradation.

- [ ] **Step 7: Smoke test — Picker scrollbar visible**

Open the Tactics panel. Select a character. Click `+ From preset`. Verify:
- A vertical scrollbar appears on the right edge of the popup IF preset count exceeds the popup height (typical 15+ presets at FontScale=1.0 on Steam Deck).
- Dragging the handle scrolls the content.
- Mouse-wheel / trackpad scroll still works (no regression).
- Click outside or press Escape closes the picker.

With a 3-preset list (delete most or test in a fresh save), verify the scrollbar auto-hides — popup looks identical to pre-change for short lists.

- [ ] **Step 8: Smoke test — Other dropdowns inherit scrollbar**

Open the rule editor on any condition. Click the Property dropdown (long enum list: HpPercent...AdjacentEnemyCount). Verify the scrollbar appears.

Quick eyeball pass: Subject dropdown, Operator dropdown, RangeBracket-value dropdown (e.g. WithinRange condition). All should show scrollbar when content overflows, hide when it doesn't.

- [ ] **Step 9: If anything fails, fix forward**

If any smoke test fails, open the failure on the deck log, reproduce on a clean save, and fix. Each fix is a new commit. Re-run from Step 1.

If everything passes, this task is complete and the branch is release-ready.

---

## Task 7: Release

**Files:** None (orchestrated by `/release` slash command).

- [ ] **Step 1: Confirm pre-bump version**

Run:
```bash
grep -E '"Version"|<Version>' WrathTactics/Info.json WrathTactics/WrathTactics.csproj
```

Expected: both files show `1.14.1` (the current master version per recent commit `a203697 chore: bump version to 1.14.1`). If either shows `1.15.0` already, you have a stale manual bump — see `wrath-mods/CLAUDE.md` "/release Pre-condition" gotcha (`git reset --hard HEAD~1` to drop the bump and let `/release` produce its own).

- [ ] **Step 2: Run /release**

Invoke `/release` (the slash command at `.claude/commands/release.md`). It will:
1. Bump `Info.json` + `WrathTactics.csproj` from 1.14.1 → 1.15.0
2. Run Release-config build → produces `WrathTactics/bin/WrathTactics-1.15.0.zip`
3. Pause at user-confirm gate — review the bumped files and the zip
4. Push, tag, GitHub Release → triggers `.github/workflows/nexus-upload.yml` → auto-upload to Nexus mod 1005
5. Generate Discord-post draft for community announcement

- [ ] **Step 3: Verify Nexus upload succeeded**

After `/release` completes, check the latest GitHub Actions run on `origin` for the `nexus-upload` workflow. Open the Nexus mod page (`https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/1005`) and confirm the 1.15.0 file appears under Files. Mod-page text (description, header image) requires manual update on Nexus — not part of this plan.

---

## Self-review

**Spec coverage check:**

Spec sections vs. tasks:
- "Executor + Validator dispatch" (Spec §Behavior) → Tasks 1, 2 ✓
- "UI gate + height calc" (Spec §UI) → Task 3 ✓
- "Scrollbar" (Spec §Scrollbar) → Task 4 ✓
- "Comment + CLAUDE.md updates" (Spec §CLAUDE.md updates) → Task 5 ✓
- "Manual smoke test on Steam Deck" (Spec §Test plan) → Task 6 ✓
- "Version bump + release via /release" (Spec §Implementation order item 6) → Task 7 ✓

All spec sections have at least one corresponding task. No gaps.

**Placeholder scan:**

No "TBD", "TODO", "fill in", or "implement later" in any task. Every code-touching step shows the exact before/after C# or markdown content. Smoke-test steps name specific log lines to look for.

**Type consistency:**

- `ResolveCastSpellChain` signature matches between Tasks 1 (executor uses it implicitly via `ExecuteCastSpell(ActionDef, ...)`) and Task 2 (validator calls it directly with `out _unused, out _unusedId`) — same `out ItemEntity, out string` signature as defined in the current `ActionValidator.Cast.cs:97`.
- `FallbackAbilityIds` is the canonical name in spec, plan, and existing code — no `Fallbacks` / `FallbackAbilities` drift.
- `chainCapable` local in Task 3 Step 3 is a new local, not referenced from any other task.
- Scrollbar field names (`verticalScrollbar`, `verticalScrollbarVisibility`, `verticalScrollbarSpacing`) are standard Unity `ScrollRect` properties — confirmed against Unity UI API.

No inconsistencies found.

---

**Estimated effort:** Tasks 1-5: ~2-3 hours of focused edits + builds. Task 6 (smoke test) depends on save-game availability for Dragon Bloodline / Mythic Dragon path — allow 30-60 min. Task 7 is automated, ~10 min if Nexus workflow runs cleanly.
