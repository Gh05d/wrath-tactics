# Out-of-Combat Tactics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make tactics rules opt-in to out-of-combat evaluation via a `Combat.IsInCombat→No` condition, with the engine ticking continuously (slower out-of-combat), so post-combat cleanup, inter-combat self-heal, and similar workflows work without single-pass limits.

**Architecture:** Continuous tick at two intervals (in-combat ≈ 0.5 s, out-of-combat ≈ 2 s). Out-of-combat ticks pre-filter rules to only those carrying a `Combat.IsInCombat==false` condition (backwards-compat). The legacy single-pass `RunPostCombatCleanup` + `IsPostCombatPass` machinery is removed; `Combat.IsInCombat` evaluates against the live game state.

**Tech Stack:** C# (.NET Framework 4.8.1), Unity 2019.4 (game-bundled), Newtonsoft.Json (game-bundled). No test framework — the mod runs inside Pathfinder: WotR. Verification is via `deploy.sh` to Steam Deck + reading mod session log + ingame behavior check.

**Spec:** `docs/superpowers/specs/2026-05-04-out-of-combat-tactics-design.md`

---

## File Map

| File | Change |
|------|--------|
| `WrathTactics/Models/TacticsConfig.cs` | Add `OutOfCombatTickIntervalSeconds` field |
| `WrathTactics/Engine/ConditionEvaluator.cs` | Remove `IsPostCombatPass` static property; simplify `IsInCombat` branch |
| `WrathTactics/Engine/TacticsEvaluator.cs` | Drop `!IsInCombat` early-return; dual-interval tick; out-of-combat rule filter; remove `RunPostCombatCleanup`, `TryExecuteRulesIgnoringCooldown`, `cooldowns.Clear()` on combat-end |
| `CLAUDE.md` | Replace "Post-combat evaluation" gotcha with "Continuous out-of-combat tick" |

No new files. No deletions. No test files (project has no test framework — verification is on-device).

---

### Task 1: Add `OutOfCombatTickIntervalSeconds` to TacticsConfig

Backwards-compatible: Newtonsoft fills missing JSON fields with the property default on deserialise. Existing user configs (`tactics-{GameId}.json`) will load with the default and pick up the new field on next save.

**Files:**
- Modify: `WrathTactics/Models/TacticsConfig.cs:9`

- [ ] **Step 1: Add the field**

In `TacticsConfig.cs` directly below the existing `TickIntervalSeconds`:

```csharp
[JsonProperty] public float TickIntervalSeconds { get; set; } = 3f;
[JsonProperty] public float OutOfCombatTickIntervalSeconds { get; set; } = 2f;
```

- [ ] **Step 2: Build**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add WrathTactics/Models/TacticsConfig.cs
git commit -m "feat(config): add OutOfCombatTickIntervalSeconds (default 2s)"
```

---

### Task 2: Remove `IsPostCombatPass` from ConditionEvaluator

The single-pass override goes away. `Combat.IsInCombat` will now reflect the live game state at every evaluation, which is correct for continuous-tick architecture.

**Files:**
- Modify: `WrathTactics/Engine/ConditionEvaluator.cs:20-27` (remove property + doc)
- Modify: `WrathTactics/Engine/ConditionEvaluator.cs:443-444` (simplify branch)

- [ ] **Step 1: Remove the property and its doc**

Delete this block from `ConditionEvaluator.cs` (the doc-comment + property declaration, currently above the rule-scoped ambient-state comment):

```csharp
        /// <summary>
        /// Set to true during TacticsEvaluator.RunPostCombatCleanup() so that
        /// `Combat.IsInCombat` evaluates to false during the one-shot cleanup pass,
        /// regardless of transient game state.
        /// </summary>
        public static bool IsPostCombatPass { get; set; }

```

- [ ] **Step 2: Simplify the `IsInCombat` branch**

In `EvaluateCombat`, change:

```csharp
            if (condition.Property == ConditionProperty.IsInCombat) {
                bool inCombat = !IsPostCombatPass && Game.Instance.Player.IsInCombat;
                bool wanted = ParseBoolValue(condition.Value);
                bool match = inCombat == wanted;
                return condition.Operator == ConditionOperator.NotEqual ? !match : match;
            }
```

to:

```csharp
            if (condition.Property == ConditionProperty.IsInCombat) {
                bool inCombat = Game.Instance.Player.IsInCombat;
                bool wanted = ParseBoolValue(condition.Value);
                bool match = inCombat == wanted;
                return condition.Operator == ConditionOperator.NotEqual ? !match : match;
            }
```

- [ ] **Step 3: Build (will FAIL — TacticsEvaluator still references the property)**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: build error along the lines of `'ConditionEvaluator' does not contain a definition for 'IsPostCombatPass'` from `TacticsEvaluator.cs` references inside `RunPostCombatCleanup`. This is expected — Task 5 removes those callers. Don't fix it here yet, just continue to Task 3.

If you accidentally don't see this error (i.e. someone already removed RunPostCombatCleanup), proceed regardless.

- [ ] **Step 4: Stage but don't commit yet**

```bash
git add WrathTactics/Engine/ConditionEvaluator.cs
```

Commit happens after Task 5 makes the tree green again — keeping the IsPostCombatPass removal as one logical commit.

---

### Task 3: Add `RuleEnabledOutOfCombat` helper

Pre-filter for out-of-combat ticks. Scans the rule's ConditionGroups for any `Combat.IsInCombat == false` condition. Looseness is intentional (see spec §"Rule Filter").

**Files:**
- Modify: `WrathTactics/Engine/TacticsEvaluator.cs` (add private static method near the top of the class)

- [ ] **Step 1: Add the helper above `EvaluateUnit`**

In `TacticsEvaluator.cs`, immediately above the existing `static void EvaluateUnit(...)` method (currently around line 68), insert:

```csharp
        // Returns true iff the rule has at least one Combat.IsInCombat==false condition
        // anywhere in its ConditionGroups. Used as the out-of-combat opt-in gate; the
        // condition's actual matching during evaluation is handled by the existing
        // bucket-AND-OR logic in ConditionEvaluator.Evaluate. Looseness is intentional —
        // presence of the condition is the user's expressed "out-of-combat-fähig" intent.
        static bool RuleEnabledOutOfCombat(TacticsRule rule) {
            if (rule.ConditionGroups == null) return false;
            foreach (var group in rule.ConditionGroups) {
                if (group?.Conditions == null) continue;
                foreach (var c in group.Conditions) {
                    if (c.Subject != ConditionSubject.Combat) continue;
                    if (c.Property != ConditionProperty.IsInCombat) continue;
                    if (c.Operator != ConditionOperator.Equal) continue;
                    var v = c.Value?.Trim().ToLowerInvariant();
                    if (v == "false" || v == "0" || v == "no" || v == "nein") return true;
                }
            }
            return false;
        }
```

(The bool-string parsing duplicates `ConditionEvaluator.ParseBoolValue` which is `private`. Duplicating ~5 lines is cheaper than widening visibility for a single caller.)

- [ ] **Step 2: Stage**

```bash
git add WrathTactics/Engine/TacticsEvaluator.cs
```

Build still red from Task 2 — green after Task 5. Don't try to build now.

---

### Task 4: Dual-interval tick + out-of-combat rule filter

Drop the `!IsInCombat` early-return. Pick the tick interval from `IsInCombat`. Pass a `bool playerInCombat` flag into `TryExecuteRules` so it can pre-filter rules during out-of-combat ticks.

**Files:**
- Modify: `WrathTactics/Engine/TacticsEvaluator.cs:21-66` (Tick body)
- Modify: `WrathTactics/Engine/TacticsEvaluator.cs:88-89` (TryExecuteRules signature)

- [ ] **Step 1: Replace the Tick body**

Replace the entire `public static void Tick(float gameTimeSec)` method (currently lines 21–66) with:

```csharp
        public static void Tick(float gameTimeSec) {
            bool inCombat = Game.Instance.Player.IsInCombat;

            // Combat-end transition: log + reset the foreign-command tracker. The legacy
            // RunPostCombatCleanup single-pass is gone — the next regular tick handles
            // cleanup with cooldowns honored.
            if (!inCombat && wasInCombat) {
                wasInCombat = false;
                PlayerCommandGuard.Reset();
                Log.Engine.Info("Combat ended");
            }

            // Combat-start transition.
            if (inCombat && !wasInCombat) {
                wasInCombat = true;
                combatStartTime = gameTimeSec;
                PlayerCommandGuard.Reset();
                Log.Engine.Info("Combat started");
                var partyNames = new List<string>();
                foreach (var u in Game.Instance.Player.PartyAndPets) {
                    partyNames.Add($"{u.CharacterName}({u.UniqueId}) inGame={u.IsInGame}");
                }
                Log.Engine.Info($"Combat party: {string.Join(", ", partyNames)}");
            }

            var config = ConfigManager.Current;
            float interval = inCombat
                ? config.TickIntervalSeconds
                : config.OutOfCombatTickIntervalSeconds;
            if (gameTimeSec - lastTickTime < interval) return;
            lastTickTime = gameTimeSec;

            tickCounter++;
            int evaluableUnits = 0;
            foreach (var u in Game.Instance.Player.PartyAndPets) {
                if (u.IsInGame && u.HPLeft > 0) evaluableUnits++;
            }
            Log.Engine.Trace($"Tick #{tickCounter} gameTime={gameTimeSec:F1}s inCombat={inCombat} evaluable={evaluableUnits}");

            if (BubbleBuffsCompat.IsExecuting()) return;

            foreach (var unit in Game.Instance.Player.PartyAndPets) {
                if (!unit.IsInGame || unit.HPLeft <= 0) continue;
                if (!config.IsEnabled(unit.UniqueId)) continue;
                EvaluateUnit(unit, config, gameTimeSec, inCombat);
            }
        }
```

- [ ] **Step 2: Update `EvaluateUnit` signature + delegation**

Replace the existing `EvaluateUnit` method (currently lines 68–86) with:

```csharp
        static void EvaluateUnit(UnitEntityData unit, TacticsConfig config, float gameTimeSec, bool inCombat) {
            // Skip if a player- (or other-mod-) issued command is currently running. Our own
            // tactics commands stay in the tracked set and don't block — self-interruption
            // when a higher-priority rule matches mid-cast is intentional (DAO semantics).
            if (PlayerCommandGuard.HasForeignActiveCommand(unit)) {
                Log.Engine.Trace($"  Skip {unit.CharacterName}: player/foreign command active");
                return;
            }

            Log.Engine.Trace($"  Evaluating {unit.CharacterName} (hp={unit.HPLeft}/{unit.Stats.HitPoints.ModifiedValue}, id={unit.UniqueId}, inCombat={inCombat})");

            var globalRules = config.GlobalRules;
            var charRules = config.GetRulesForCharacter(unit.UniqueId);

            if (TryExecuteRules(globalRules, unit, "global", gameTimeSec, inCombat))
                return;
            TryExecuteRules(charRules, unit, unit.CharacterName, gameTimeSec, inCombat);
        }
```

- [ ] **Step 3: Update `TryExecuteRules` signature + add the out-of-combat filter**

Replace the existing `TryExecuteRules` method (currently lines 88–133) with:

```csharp
        static bool TryExecuteRules(List<TacticsRule> rules, UnitEntityData unit,
            string source, float gameTimeSec, bool inCombat) {
            for (int i = 0; i < rules.Count; i++) {
                var entry = rules[i];
                if (!entry.Enabled) continue;

                var rule = PresetRegistry.Resolve(entry);

                // Out-of-combat opt-in gate. Rules without a Combat.IsInCombat==false
                // condition keep their pre-1.x.x behavior (in-combat-only).
                if (!inCombat && !RuleEnabledOutOfCombat(rule)) {
                    continue;
                }

                // Check cooldown — key on entry.Id so linked copies cooldown independently
                var cooldownKey = (unit.UniqueId, entry.Id);
                float cooldownSec = rule.CooldownRounds * 6f;
                if (cooldowns.TryGetValue(cooldownKey, out float lastFired)) {
                    if (gameTimeSec - lastFired < cooldownSec) {
                        Log.Engine.Trace($"{unit.CharacterName} Rule {i} \"{rule.Name}\": on cooldown ({gameTimeSec - lastFired:F1}s / {cooldownSec:F0}s)");
                        continue;
                    }
                }

                ConditionEvaluator.ClearMatchedEntities();

                bool match = ConditionEvaluator.Evaluate(rule, unit);
                if (!match) {
                    Log.Engine.Trace($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): conditions not met");
                    continue;
                }

                var target = TargetResolver.Resolve(rule.Target, unit);

                if (!ActionValidator.CanExecute(rule.Action, unit, target)) {
                    Log.Engine.Warn($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): MATCH but action not executable");
                    continue;
                }

                if (CommandExecutor.Execute(rule.Action, unit, target)) {
                    cooldowns[cooldownKey] = gameTimeSec;
                    Log.Engine.Info($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): EXECUTED -> {FormatTarget(target)}");
                    return true;
                }
            }
            return false;
        }
```

- [ ] **Step 4: Stage**

```bash
git add WrathTactics/Engine/TacticsEvaluator.cs
```

Build is still red — we still reference `ConditionEvaluator.IsPostCombatPass` from `RunPostCombatCleanup`. Task 5 fixes that.

---

### Task 5: Remove `RunPostCombatCleanup` + `TryExecuteRulesIgnoringCooldown` + `cooldowns.Clear()`

This brings the tree back to green and concludes the IsPostCombatPass removal as one commit (Tasks 2–5).

**Files:**
- Modify: `WrathTactics/Engine/TacticsEvaluator.cs:135-` (delete two methods)

- [ ] **Step 1: Delete `RunPostCombatCleanup`**

Find the method `static void RunPostCombatCleanup(float gameTimeSec)` and delete the entire method (signature, body, and trailing brace).

- [ ] **Step 2: Delete `TryExecuteRulesIgnoringCooldown`**

Find the method `static bool TryExecuteRulesIgnoringCooldown(List<TacticsRule> rules, UnitEntityData unit, string source, float gameTimeSec)` and delete it entirely. (It was only called from `RunPostCombatCleanup`.)

- [ ] **Step 3: Build**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. If you get errors, the most likely cause is a stale `cooldowns.Clear()` line that I missed mentioning — Task 4's Tick rewrite already drops it, but double-check there is no orphan `cooldowns.Clear()` remaining anywhere in the file.

- [ ] **Step 4: Stage and commit Tasks 2–5 together**

```bash
git add WrathTactics/Engine/ConditionEvaluator.cs WrathTactics/Engine/TacticsEvaluator.cs
git commit -m "$(cat <<'EOF'
feat(engine): continuous out-of-combat tick with opt-in via IsInCombat condition

Replace the one-shot RunPostCombatCleanup + IsPostCombatPass single-pass with
a continuous tick that runs out-of-combat at a slower interval (default 2s).
Rules with a Combat.IsInCombat==false condition somewhere in their
ConditionGroups become eligible out-of-combat; rules without that condition
keep their in-combat-only behavior unchanged.

Removes:
- ConditionEvaluator.IsPostCombatPass (the single-pass override flag)
- TacticsEvaluator.RunPostCombatCleanup (the one-shot pass)
- TacticsEvaluator.TryExecuteRulesIgnoringCooldown (only used by RunPostCombatCleanup)
- cooldowns.Clear() on combat-end (cooldowns now operate purely in real-time, consistent across the boundary)

Adds:
- TacticsEvaluator.RuleEnabledOutOfCombat helper (out-of-combat opt-in gate)
- Dual-interval tick: TickIntervalSeconds in-combat, OutOfCombatTickIntervalSeconds out-of-combat

The Combat.IsInCombat condition now matches against the live game state at
every evaluation, which is correct for continuous-tick architecture.
EOF
)"
```

---

### Task 6: Update CLAUDE.md gotcha

The existing "Post-combat evaluation" entry describes the now-removed single-pass mechanism. Replace it with notes about the continuous-tick architecture and the out-of-combat opt-in gate.

**Files:**
- Modify: `CLAUDE.md` (the bullet currently starting `**Post-combat evaluation**`)

- [ ] **Step 1: Find the old gotcha**

In `CLAUDE.md`, locate the bullet:

```
- **Post-combat evaluation**: `TacticsEvaluator.Tick` early-returns when `!Player.IsInCombat`. To let rules fire on the combat-end transition, `RunPostCombatCleanup()` runs a single evaluation pass with `ConditionEvaluator.IsPostCombatPass = true`, which makes `Combat.IsInCombat == false` conditions match regardless of transient game state. Cooldowns are skipped in this pass and cleared immediately after.
```

- [ ] **Step 2: Replace it with the new gotcha**

Replace the entire bullet with:

```
- **Continuous out-of-combat tick** (since 1.7.0): `TacticsEvaluator.Tick` runs in both states. Out-of-combat the interval is `TacticsConfig.OutOfCombatTickIntervalSeconds` (default 2 s, JSON-only); in-combat it's the existing `TickIntervalSeconds`. Out-of-combat ticks pre-filter rules through `RuleEnabledOutOfCombat(rule)` — only rules carrying a `Combat.IsInCombat==false` condition somewhere in their ConditionGroups pass the gate. Rules without that condition stay in-combat-only (backwards-compat with all pre-1.7.0 rules and seeded defaults). Cooldowns operate purely in real-time across the combat boundary — no `cooldowns.Clear()` on combat-end any more, so a heal that fired in the last second of combat won't immediately re-fire on the cleanup tick. Predecessor mechanism (`IsPostCombatPass` single-pass + `RunPostCombatCleanup` + `TryExecuteRulesIgnoringCooldown`) has been removed entirely; do not look for it in IL.
```

- [ ] **Step 3: Build (sanity, no code change but ensures nothing else is broken)**

```bash
~/.dotnet/dotnet build WrathTactics/WrathTactics.csproj -p:SolutionDir=$(pwd)/
```

Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```bash
git add CLAUDE.md
git commit -m "docs(claude): replace post-combat single-pass gotcha with continuous-tick note"
```

---

### Task 7: Smoke test on Steam Deck

This is where the design earns its keep. No automation possible — it's a Unity mod targeting a closed-source RPG running through Proton on a Steam Deck. Manual checks against the Verification section of the spec.

**Files:**
- Run: `./deploy.sh`
- Inspect: `<game>/Mods/WrathTactics/Logs/wrath-tactics-<latest>.log`
- Test in-game

- [ ] **Step 1: Deploy**

```bash
./deploy.sh
```

Expected: `Deployed to Steam Deck.`

- [ ] **Step 2: Verify the deployed DLL is the just-built one**

```bash
ssh deck-direct "stat -c '%y' '/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/WrathTactics/WrathTactics.dll'"
ls -l WrathTactics/bin/Debug/WrathTactics.dll
```

The deck mtime should match (or be newer than) the local build mtime. If not, deploy didn't actually copy — re-run `./deploy.sh`. (See parent CLAUDE.md "Verify deploy before diagnosing".)

- [ ] **Step 3: Boot the game on Steam Deck and load any save**

The user runs the game manually. The mod should load with the new tick architecture. Mod session log header should show `Wrath Tactics loading` and end with `Wrath Tactics loaded.` — no exception.

- [ ] **Step 4: Set up a test rule on a healer character**

In the Tactics panel (Ctrl+T or HUD button):
1. Pick a healer (e.g. Cleric).
2. Add a new rule:
   - **IF:** `Self.HpPercent < 80%` AND `Combat.IsInCombat = No`
   - **THEN:** `Heal` (mode: `Any`, energy: `Auto`, sources: `All sources`)
   - **TARGET:** `Self`
   - **Cooldown:** `1` round (6 s)
3. Make sure the character has tactics enabled.

- [ ] **Step 5: Verify in-combat behavior unchanged**

Take damage in combat. The rule must **not** fire (the `IsInCombat=No` condition is false in combat → conditions don't match). Tactics that DO fire in-combat are existing rules without the new condition — confirm they still work as before.

Mod session log: in-combat ticks at `TickIntervalSeconds` cadence (Trace lines `Tick #N gameTime=... inCombat=True`).

- [ ] **Step 6: Verify out-of-combat heal fires**

End combat with party at <80% HP. Within ~2 s, the healer auto-casts a heal on themselves. If the heal isn't enough to clear the 80% threshold, the rule fires again every 6 s real-time (cooldown) until HP > 80% or resources are exhausted.

Mod session log: should see `Combat ended` (no `post-combat cleanup ran` line — that's gone), then Trace lines with `inCombat=False` and an `EXECUTED -> ...` line for the heal cast.

- [ ] **Step 7: Verify backwards-compat for existing rules**

Walk to a different area where the seeded `Emergency Self-Heal` preset would normally fire if HP is low (the preset has no `Combat.IsInCombat=No` condition, only `Self.HpPercent<30`). Bring a character below 30% HP out of combat (e.g. via fall damage, then end combat).

The seeded rule must **not** fire out of combat — it has no opt-in condition. If it fires, the filter is broken.

In-combat with HP < 30, the rule must fire normally — this verifies the filter doesn't accidentally block in-combat work.

- [ ] **Step 8: Verify configurable interval**

Edit `<game>/Mods/WrathTactics/UserSettings/tactics-{GameId}.json` on the deck:

```bash
ssh deck-direct "grep -n 'OutOfCombatTickIntervalSeconds\|TickIntervalSeconds' '/run/media/deck/3b03f019-ee3d-473e-beb1-98236afc5254/steamapps/common/Pathfinder Second Adventure/Mods/WrathTactics/UserSettings/tactics-<GameId>.json'"
```

The field should exist with value `2.0` (default). Edit it to `5.0`, save the game (forces config reload), end combat. Verify the next out-of-combat trace tick is ~5 s after combat end, not ~2 s.

Restore to `2.0` after the test.

- [ ] **Step 9: If smoke test fails**

Pull the latest mod session log:

```bash
ssh deck-direct "ls -t '/run/media/deck/.../Mods/WrathTactics/Logs/' | head -1"
ssh deck-direct "cat '/run/media/deck/.../Mods/WrathTactics/Logs/<that file>'"
```

Trace lines tell you when ticks fire and which rules match/fail. If the rule never fires out-of-combat, check:
1. Did `RuleEnabledOutOfCombat` return true for it? (Add a temporary `Log.Engine.Debug($"OoC gate: rule={rule.Name} pass=true/false")` if needed.)
2. Did the cooldown filter block it? (Trace line `on cooldown`.)
3. Did the validator reject the heal? (Warn line `MATCH but action not executable`.)

Address before declaring the task complete.

- [ ] **Step 10: When the smoke test passes, commit a release-readiness marker**

Nothing to commit here per se — the code is already committed in earlier tasks. This step's deliverable is human confirmation that the spec's verification checklist passed. Move to the release flow (`/release`) when ready.

---

## Self-Review Checklist (run after writing the plan)

- **Spec coverage:** Tick loop ✓ (Task 4), out-of-combat filter ✓ (Tasks 3, 4), `IsPostCombatPass` removal ✓ (Tasks 2, 5), `RunPostCombatCleanup` removal ✓ (Task 5), config field ✓ (Task 1), CLAUDE.md update ✓ (Task 6), verification steps ✓ (Task 7).
- **Placeholders:** none — every step has the actual code or command.
- **Type consistency:** `RuleEnabledOutOfCombat` referenced in Task 4 with the same signature defined in Task 3. `TryExecuteRules`/`EvaluateUnit` signatures are consistent across Tasks 4. `OutOfCombatTickIntervalSeconds` field name consistent in Tasks 1, 4, 6, 7.
- **Edge cases from spec:** cutscenes/dialog (spec accepts engine-side cast blocking), camp (rules fire — desired), inventory screen (game blocks input — fine), combat-end-frame race (cooldowns no longer cleared, new line in Task 4 + Task 5), zero-CD heal spam (resource cost limits), PlayerCommandGuard (untouched, retained reset on combat-end transition in Task 4). All handled.
