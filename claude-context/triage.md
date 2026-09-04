# Triage: Bug Reports, Logs, "Rule Didn't Fire"

Recipes for diagnosing user reports and deck-side behavior. General bug-report protocol (no-log reproduction from blueprints, UMM-off test): parent `wrath-mods/CLAUDE.md` §Working Style.

## Before Diagnosing Anything

- **Verify deploy before diagnosing "doesn't work"**: `ssh deck-direct stat` on deployed DLL vs. local source mtime. Older deck DLL = fix isn't on system under test. ([deep-dive](../docs/wrath-api-deep-dive.md#verify-deploy))
- **Phantom log lines on deck**: deck DLL = last `./deploy.sh`'d, not HEAD. Trace message that doesn't `grep` in source = leftover instrumentation; re-deploy clean before trusting deck logs.
- **Check master vs. user-reported version first**: user reports often lag HEAD by 1–2 releases. Run `git log v<user-version>..HEAD --oneline -- <suspect-file>` before Phase-1 investigation — bug may already be fixed.

## Log Locations & Recipes

- **Mod session logs**: `<game>/Mods/WrathTactics/Logs/wrath-tactics-YYYY-MM-DD-HHMMSS.log` (separate from `Player.log`). Latest: `ssh deck-direct "ls -t '<game>/Mods/WrathTactics/Logs/' | head -1"`.
- **`"MATCH but action not executable"` is the triage smoking gun for "Rule didn't fire" reports**: rule matched, target resolved, but `ActionValidator.CanExecute` rejected — most commonly `"No suitable spell slots"`. Grep for it BEFORE asking the user for setup details — most reports resolve to "spell not prepared".
- **Global-preemption semantics for "rules never fire" triage**: global rules skip character rules only on successful EXECUTION (or while the issued command still runs, `ActiveRuleTracker` CharGate=0) — a global rule that matches but fails validation ("MATCH but action not executable") does NOT block character rules.
- **For CastSpell rules also grep `engine-unavailable` and `no available slots`** (TRACE-level): fired from `FindCastSpellSource` when an ability matched but isn't currently castable. `engine-unavailable` carries `GetUnavailableReason()` (silenced, polymorphed, opposition school, UMD fail); `no available slots` carries the variant GUID and metamagic mask.
- **"Spell switches to another mid-cast" / "wrong rule fired" reports are by-design preemption, not a bug**: a lower-list rule fires while higher-list rules are on cooldown; once a higher rule's (usually longer) cooldown expires mid-windup it interrupts the in-flight cast (`ActiveRuleTracker` gate). Fix = reorder. Reconstruct what fired via log "Rule N" (= array index) cross-referenced with the live config `UserSettings/tactics-{GameId}.json`.
- **"Cast restarts before finishing" (gleiche Ability, Loop)**: 3 Formen — (A) verlorene Tracker-Referenz: `RunVerified` wertet als Veto/Merge-Miss, Engine castet aber → kein Cooldown-Stempel, kein Gate → Re-Issue jeden Tick (Log: dieselbe "Rule N" jeden Tick + "Commands.Run discarded/merged"-Warnungen); (B) Zwei-Regel-Ping-Pong via by-design Preemption (Log: alternierende Rule N); (C) `CooldownRounds=0` / nie-falsch-werdende Condition → Re-Cast nach jedem Abschluss statt mid-windup (Cooldown stempelt bei Cast-START, `TacticsEvaluator.cs:175`). A vs. B nur per Log unterscheidbar; Report 2026-07 war B (User-Config, Neuaufbau löste es).
- **"Heal didn't fire" triage**: grep `AllyBucket miss: count=N GreaterOrEqual threshold=M` — count-subject heal rules (e.g. `AllyCount HpPercent ≤ X`, threshold 2) need ≥2 wounded allies in range; one wounded ally never triggers, and with no single-target heal rule nothing fires. Config, not code.
- **"Rule never fires out of combat" (e.g. auto-clear a persistent debuff like Death's Door while exploring)**: rules are IN-COMBAT-ONLY unless the rule contains a `Combat: IsInCombat = No` condition — the out-of-combat opt-in gate (`TacticsEvaluator.cs:146`, `RuleEnabledOutOfCombat`). Adding it to the SAME group is an AND (fires only OOC); for both states use two OR-groups (`IsInCombat = No` / `IsInCombat = Yes`). Note Death's Door is `UnitCondition.DeathDoor` (=5) — a persistent affliction applied by `UnitLifeController` on a deadly injury (when the difficulty's Death's Door setting is on), removed by **rest** (`RestController`) or Greater Restoration (`ContextActionRemoveDeathDoor`); it persists out of combat, so `HasCondition = Death's Door` detects it correctly — the OOC gate is the usual reason such a rule "never fires".
- **"Spell X missing from picker" triage**: ask which spellbook list it lives in — `GetKnownSpells` / `GetCustomSpells` / `GetSpecialSpells` are three different code paths; bugs usually affect one. ([deep-dive](../docs/wrath-api-deep-dive.md#variant-component-handling))

### "Rule didn't fire" — slot-economy causes (v1.29.0+)

A unit can now spend one command per action slot per tick. Two of the skip lines are legitimate and must not be chased as bugs:

```
<Name> Rule 2 "Attack" (Character): slot Standard already used this tick
<Name> Rule 4 "Judgment" (Character): slot Swift busy (UnitUseAbility in flight)
```

- **`slot X already used this tick`** — an earlier rule already spent that slot. The rule fires on a later tick. Working as intended; if the user wants the *other* rule instead, they need to reorder.
- **`slot X busy (UnitUseAbility in flight)`** — a non-Standard rule's own previous command is still running. Move/swift commands are near-instantaneous, so a persistent occurrence means something else holds that slot.
- **`gated by active rule (limit N)`** — the classic priority gate; since v1.29.0 it only ever appears on Standard-slot rules. If it shows up on a rule the user believes is a move or swift action, the ability's `RuntimeActionType` disagrees with them (Quicken, mythic flags) or the classification fell back to Standard because the `AbilityData` did not resolve.

The `EXECUTED` line now carries the slot: `EXECUTED [Move] -> Ember`. `[no-slot]` means `ToggleActivatable`, which claims no slot at all.

## Silent Freezes

- **Panel rendered but unresponsive, no log output** ⇒ suspect `StackOverflowException` (uncatchable, kills Unity main thread silently). Diagnose via code search for self-recursion, not via logs — see [`gotchas-persistence.md`](gotchas-persistence.md) (`PersistEdit` precedent).
- **`Current HP` (HpFlat) matches dead allies at 0**: hot path clamps negative `HPLeft` to 0, so `Ally · Current HP · <= X` also matches corpses — same semantics as `HpPercent`. Intended exclusion is an additional `IsDead != Yes` row; Enemy scope is unaffected (`GetVisibleEnemies` filters `HPLeft > 0`).
