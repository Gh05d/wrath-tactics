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
- **"Heal didn't fire" triage**: grep `AllyBucket miss: count=N GreaterOrEqual threshold=M` — count-subject heal rules (e.g. `AllyCount HpPercent ≤ X`, threshold 2) need ≥2 wounded allies in range; one wounded ally never triggers, and with no single-target heal rule nothing fires. Config, not code.
- **"Spell X missing from picker" triage**: ask which spellbook list it lives in — `GetKnownSpells` / `GetCustomSpells` / `GetSpecialSpells` are three different code paths; bugs usually affect one. ([deep-dive](../docs/wrath-api-deep-dive.md#variant-component-handling))

## Silent Freezes

- **Panel rendered but unresponsive, no log output** ⇒ suspect `StackOverflowException` (uncatchable, kills Unity main thread silently). Diagnose via code search for self-recursion, not via logs — see [`gotchas-persistence.md`](gotchas-persistence.md) (`PersistEdit` precedent).
