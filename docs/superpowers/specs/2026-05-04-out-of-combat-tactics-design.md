# Out-of-Combat Tactics — Design

## Context

Today `TacticsEvaluator.Tick` early-returns when `!Player.IsInCombat`. The single concession to post-combat work is `RunPostCombatCleanup`, a one-shot pass on the combat-end transition that flips `ConditionEvaluator.IsPostCombatPass=true` so `Combat.IsInCombat==false` conditions match for that single tick. Cooldowns are skipped and immediately cleared.

That's enough for "one heal per char on combat end" and nothing else. Multi-step cleanup — resurrect-then-heal-then-dispel sequences, or a slow heal that needs several casts to top a char off — is impossible. The Nexus reporter built a `Combat.IsInCombat → No` rule expecting it to fire, watched it fire **once**, and reported it as "doesn't work."

Goal: make tactics rules usable for post-combat cleanup, inter-combat self-heal, and any other "I want this to fire when not in a fight" workflow, without changing in-combat behavior for existing rules.

## Approach

**Continuous tick at a slower interval, opt-in per rule via `Combat.IsInCombat→No`.**

The evaluator runs every tick regardless of combat state. In-combat the interval stays at 0.5 s (today's default). Out-of-combat it ticks at a new, slower interval (default 2.0 s) — enough latency to keep the eval loop cheap during exploration, fast enough that post-combat cleanup feels immediate.

Rules opt in to out-of-combat by adding a `Combat.IsInCombat==false` condition somewhere in their ConditionGroups. Rules without that condition are simply not evaluated during out-of-combat ticks — backwards-compatible.

The `IsPostCombatPass` single-pass mechanism is removed; with continuous evaluation, `Combat.IsInCombat` reflects the real game state at tick time and conditions match naturally.

## Components

### `TacticsEvaluator.Tick`

The early-return on `!Player.IsInCombat` goes away. The tick interval is selected from config based on combat state:

```
float interval = Player.IsInCombat
    ? config.TickIntervalSeconds
    : config.OutOfCombatTickIntervalSeconds;
if (gameTimeSec - lastTickTime < interval) return;
```

Combat-start/end transitions retain their logging and `PlayerCommandGuard.Reset()` calls. The combat-end transition no longer triggers `RunPostCombatCleanup` and no longer clears cooldowns — the next regular tick (≤ `OutOfCombatTickIntervalSeconds` later) handles cleanup with cooldowns honored as elsewhere.

### Out-of-combat rule filter

A new helper `RuleEnabledOutOfCombat(TacticsRule rule)`:

```
return rule.ConditionGroups.Any(group =>
    group.Conditions.Any(c =>
        c.Subject == ConditionSubject.Combat
        && c.Property == ConditionProperty.IsInCombat
        && c.Operator == ConditionOperator.Equal
        && ParseBoolValue(c.Value) == false));
```

Called by `TryExecuteRules` as a pre-filter when `!Player.IsInCombat`. The presence of the condition anywhere in any group is sufficient — its actual matching during evaluation is handled by the existing bucket-AND-OR logic. Looseness is intentional: the user expressed "out-of-combat-fähig" intent by adding the condition; the engine doesn't second-guess that intent based on group composition.

### `ConditionEvaluator`

- Remove `IsPostCombatPass` property.
- In `EvaluateUnitProperty`, the `IsInCombat` branch becomes `bool inCombat = Game.Instance.Player.IsInCombat;` — drop the `IsPostCombatPass` override.

Both reference sites:
- `ConditionEvaluator.cs:24-27` (the doc + property)
- `ConditionEvaluator.cs:443-444` (the IsInCombat branch)

### `TacticsEvaluator` removals

- `RunPostCombatCleanup` method — unused after the early-return goes.
- `TryExecuteRulesIgnoringCooldown` — was only called from RunPostCombatCleanup.
- The `cooldowns.Clear()` call on the combat-end transition.

The `wasInCombat` field stays — drives the combat-start/end transition logging and `PlayerCommandGuard.Reset()`.

### `TacticsConfig`

- New field `float OutOfCombatTickIntervalSeconds = 2.0f;` with Newtonsoft default-on-missing semantics (existing configs deserialize with the default).
- Persisted alongside `TickIntervalSeconds` in `tactics-{GameId}.json`.
- No UI exposure for this release — JSON-edit only. UI can come later if users complain about the default.

### `Combat.IsInCombat` condition value semantics

Already correct (Yes/No dropdown via `bool.yes` / `bool.no` keys, persisted as `"true"` / `"false"` strings, parsed by `ParseBoolValue`). No change.

## Data Flow

```
Tick fires
  ↓
Player.IsInCombat ?
  ├── true:  interval = config.TickIntervalSeconds (0.5 s)
  └── false: interval = config.OutOfCombatTickIntervalSeconds (2.0 s)
  ↓
gameTimeSec - lastTickTime < interval ? → return
  ↓
For each party-and-pet unit (alive, in-game, tactics enabled):
  ↓
  Player.IsInCombat false ?
    ├── yes: skip rules where !RuleEnabledOutOfCombat(rule)
    └── no:  evaluate all enabled rules
  ↓
  evaluate rules → resolve target → validate → execute (unchanged)
```

Cooldowns operate purely on `gameTimeSec`, ignoring combat state — same code path as today.

## Migration

Existing rules: zero behavior change. None have `Combat.IsInCombat→No`-conditions today (the reporter's attempt notwithstanding); the out-of-combat filter rejects them and the in-combat path is byte-identical.

Existing default presets in `DefaultPresets.Build()`: none currently use `Combat.IsInCombat`. No migration needed.

User configs (`tactics-{GameId}.json`): forward-compatible. Newtonsoft fills `OutOfCombatTickIntervalSeconds` with the default 2.0 on first load; the file gets the field on next save.

## Edge Cases

- **Cutscenes / dialog scenes**: `Player.IsInCombat` is false. Out-of-combat ticks fire. Rules with `IsInCombat→No` will evaluate — and may try to cast. The game itself usually blocks casting during cutscenes; if a cast goes through it's the user's responsibility (they explicitly opted in via the condition). No special handling.
- **Rest / camp**: same as exploration — rules tick normally. Healing rules will work, which is the user-desired behavior.
- **Inventory / map / spellbook screens**: `Player.IsInCombat` is false. Tick fires, casts may queue. The game blocks input on these screens; queued casts execute on screen close. Acceptable — matches in-combat behavior when player opens inventory mid-fight.
- **Combat-end-frame race**: combat transitions to false at frame N. `wasInCombat=true → false` block runs (logging only now). Next tick at frame N + ≤2 s honors cooldowns from in-combat. A heal that fired at the very end of combat won't re-fire until its CD elapses — this is correct.
- **Zero-cooldown rules out-of-combat**: e.g. `Self.HpPercent < 50 → Cure Light Wounds` with CD 0. After a cast the heal's resource cost limits re-firing (one less spell slot per tick). Once slots exhaust, `ActionValidator.IsAvailable` rejects further attempts. The rule effectively self-throttles.
- **PlayerCommandGuard out-of-combat**: continues to work (refcounts our own casts, blocks foreign casts). User-issued out-of-combat casts (e.g. clicking a wand) preempt tactics — same as in-combat.

## Verification

Steam Deck end-to-end test (`./deploy.sh`):

1. Create a global rule: `Self.HpPercent < 80%` AND `Combat.IsInCombat → No` → `Heal` (mode: Any, source: All).
2. Take damage in combat, end combat with party at <80% HP.
3. **Expect:** within 2 s of combat end, casters auto-heal until HP > 80% or resources exhaust. Multi-cast sequences (cleric heals tank, then mage, then self) work — not just one cast.
4. **Expect:** during combat, the rule does **not** fire (combat path, `IsInCombat→Yes`-effective semantics are preserved by the AND with the now-false `IsInCombat→No` condition).
5. **Expect:** existing rules without `Combat.IsInCombat→No` (e.g. the seeded `Emergency Self-Heal` preset) only fire in combat. No change vs. 1.6.x.
6. Mod session log: in-combat ticks at 0.5 s, out-of-combat at 2.0 s. `Combat ended` log line still appears, no `post-combat cleanup ran` line (removed).
7. Edit `tactics-{GameId}.json` to set `OutOfCombatTickIntervalSeconds: 5.0`. Reload save. Verify slower out-of-combat tick cadence in log.

## Files Touched

- `WrathTactics/Engine/TacticsEvaluator.cs` — tick loop, remove RunPostCombatCleanup + TryExecuteRulesIgnoringCooldown, add out-of-combat rule filter call.
- `WrathTactics/Engine/ConditionEvaluator.cs` — remove `IsPostCombatPass`, simplify `IsInCombat` branch.
- `WrathTactics/Models/TacticsConfig.cs` (or wherever the config struct lives) — add `OutOfCombatTickIntervalSeconds`.
- `CLAUDE.md` — replace "Post-combat evaluation" gotcha with "Continuous out-of-combat tick" note describing the new behavior and `RuleEnabledOutOfCombat` filter.

## Out of Scope

- UI for `OutOfCombatTickIntervalSeconds` — JSON-only this release.
- A separate "is in cutscene" guard — relying on game-side cast blocking instead.
- Pre-combat buffing optimisations — BubbleBuffs handles that domain; tactics is reactive, not proactive.
- Per-rule out-of-combat-only flag — opt-in via condition is sufficient and avoids two redundant control surfaces.
