using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using WrathTactics.Compatibility;
using WrathTactics.Logging;
using WrathTactics.Models;
using WrathTactics.Persistence;

namespace WrathTactics.Engine {
    public static class TacticsEvaluator {
        static float lastTickTime;
        static float combatStartTime;
        static bool wasInCombat;
        static bool forceNextTick;
        static int tickCounter;

        // Per-rule cooldown tracking: (unitId, ruleId) -> last fire game time
        static readonly Dictionary<(string, string), float> cooldowns = new Dictionary<(string, string), float>();

        public static void Tick(float gameTimeSec) {
            bool inCombat = Game.Instance.Player.IsInCombat;

            // Combat-end transition: log + reset the foreign-command tracker. The legacy
            // RunPostCombatCleanup single-pass is gone — the next regular tick handles
            // cleanup with cooldowns honored.
            if (!inCombat && wasInCombat) {
                wasInCombat = false;
                PlayerCommandGuard.Reset();
                ActiveRuleTracker.Reset();
                Log.Engine.Info("Combat ended");
            }

            // Combat-start transition.
            if (inCombat && !wasInCombat) {
                wasInCombat = true;
                combatStartTime = gameTimeSec;
                // Fire the first combat tick immediately instead of waiting up to
                // TickIntervalSeconds — otherwise the party stands around for the
                // remainder of the previous tick window after combat begins.
                forceNextTick = true;
                PlayerCommandGuard.Reset();
                ActiveRuleTracker.Reset();
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
            if (!forceNextTick && gameTimeSec - lastTickTime < interval) return;
            forceNextTick = false;
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

        static void EvaluateUnit(UnitEntityData unit, TacticsConfig config, float gameTimeSec, bool inCombat) {
            // Foreign-cast gate: player- or other-mod-issued casts in the Standard slot
            // suppress evaluation entirely. Orthogonal to our own in-flight rule (handled
            // below via ActiveRuleTracker).
            if (PlayerCommandGuard.HasForeignActiveCommand(unit)) {
                Log.Engine.Trace($"  Skip {unit.CharacterName}: player/foreign command active");
                return;
            }

            var globalRules = config.GlobalRules;
            var charRules = config.GetRulesForCharacter(unit.UniqueId);

            // Priority gate: while our own previously-issued UnitCommand is still in flight,
            // only rules strictly above it in the global-then-character priority order may
            // preempt. Lower-priority matches wait until the current command finishes.
            int globalGate = int.MaxValue;
            int charGate = int.MaxValue;
            RefundVanishedCommands(unit);
            var active = ActiveRuleTracker.GetActive(unit);
            if (active.HasValue) {
                var res = ActiveRuleTracker.Resolve(
                    active.Value.Source, active.Value.EntryId, globalRules, charRules);
                if (res.Stale) {
                    // Entry was deleted/edited out of its list mid-combat — drop the gate.
                    ActiveRuleTracker.Clear(unit);
                } else {
                    globalGate = res.GlobalGate;
                    charGate = res.CharGate;
                    Log.Engine.Trace($"  {unit.CharacterName}: active rule {active.Value.Source}/{active.Value.EntryId} -> gate G<{globalGate} C<{charGate}>");
                }
            }

            Log.Engine.Trace($"  Evaluating {unit.CharacterName} (hp={unit.HPLeft}/{unit.Stats.HitPoints.ModifiedValue}, id={unit.UniqueId}, inCombat={inCombat})");

            // Per-tick action-slot budget, indexed by (int)UnitCommand.CommandType
            // (Free = 0, Standard = 1, Swift = 2, Move = 3). Shared across the global and
            // character lists so a standard-action rule in the global list still blocks a
            // standard-action rule in the character list within the same tick.
            var slotUsed = new bool[4];

            // Toggle rules issue no command, so the slot budget cannot bound them. Bound them
            // per activatable instead — see the togglesUsed check in TryExecuteRules.
            var togglesUsed = new HashSet<string>();

            // TryExecuteRules now returns "stop evaluating this unit", not "something fired".
            if (TryExecuteRules(globalRules, unit, RuleListSource.Global, gameTimeSec, inCombat, globalGate, slotUsed, togglesUsed))
                return;
            TryExecuteRules(charRules, unit, RuleListSource.Character, gameTimeSec, inCombat, charGate, slotUsed, togglesUsed);
        }

        // Returns true when evaluation of this unit must stop for the whole tick
        // (DoNothing only). A successful execution no longer ends the pass — the loop
        // continues so the unit can spend its remaining action slots.
        static bool TryExecuteRules(List<TacticsRule> rules, UnitEntityData unit,
            RuleListSource source, float gameTimeSec, bool inCombat, int priorityLimit,
            bool[] slotUsed, HashSet<string> togglesUsed) {
            for (int i = 0; i < rules.Count; i++) {
                var entry = rules[i];
                if (!entry.Enabled) continue;

                var rule = PresetRegistry.Resolve(entry);

                // Out-of-combat opt-in gate. Rules without a Combat.IsInCombat==false
                // condition keep their pre-1.7.0 behavior (in-combat-only).
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

                // The slot is only known once the AbilityData is resolved, so validation
                // must run before the gate and budget checks.
                if (!ActionValidator.CanExecute(rule.Action, unit, target, out var abilitySlot)) {
                    Log.Engine.Warn($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): MATCH but action not executable");
                    continue;
                }

                var slot = ActionSlots.Classify(rule.Action.Type, abilitySlot);

                // Priority gate: only Standard-slot rules participate in DAO-style
                // preemption, because ActiveRuleTracker only ever records those.
                // Move/swift/free rules are exempt — that is the whole feature.
                if (ActionSlots.IsGated(slot) && i >= priorityLimit) {
                    Log.Engine.Trace($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): gated by active rule (limit {priorityLimit})");
                    continue;
                }

                // Per-tick budget: one command per slot. Without it two swift rules in the
                // same tick would destroy each other via InterruptAndRemoveCommand(Swift).
                if (slot.HasValue && slotUsed[(int)slot.Value]) {
                    Log.Engine.Trace($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): slot {slot.Value} already used this tick");
                    continue;
                }

                // Toggle rules claim no slot, so the budget above cannot bound them, and an
                // "X on" / "X off" pair on the SAME activatable can both match in one tick
                // (e.g. a mixed enemy group satisfying "any enemy is a demon" and "any enemy
                // is not"). Before per-slot evaluation the upper rule won because the tick
                // ended on it; without this guard the lower rule would silently overwrite it.
                // Keyed per ability, so unrelated toggles still fire in the same tick.
                if (rule.Action.Type == ActionType.ToggleActivatable
                    && !string.IsNullOrEmpty(rule.Action.AbilityId)
                    && togglesUsed.Contains(rule.Action.AbilityId)) {
                    Log.Engine.Trace($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): activatable already toggled this tick");
                    continue;
                }

                // Cross-tick self-interrupt guard. Non-Standard rules leave no tracker entry,
                // so without this they would re-fire next tick and cut off their own
                // still-running command.
                if (slot.HasValue && !ActionSlots.IsGated(slot) && IsSlotBusyWithAbility(unit, slot.Value)) {
                    Log.Engine.Trace($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): slot {slot.Value} busy (UnitUseAbility in flight)");
                    continue;
                }

                // Cross-slot animation guard (v1.29.1). Two animated commands (UnitUseAbility /
                // UnitAttack) must never run at the same time on one unit — the second one's
                // animation releases the first's, and the engine interrupts a command whose
                // animation "is done but not acted". The engine only serialises Standard-
                // behind-running-Move; every other overlap is ours to prevent. Applies to
                // Standard rules too: a Standard cast issued over a pending Move ability
                // would be cut down the moment that Move's cooldown expires.
                if (slot.HasValue && ActionSlots.IssuesAnimatedCommand(rule.Action.Type)
                    && HasCrossSlotConflict(unit, slot.Value, out var conflictReason)) {
                    Log.Engine.Trace($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): unit busy — {conflictReason}");
                    continue;
                }

                if (CommandExecutor.Execute(rule.Action, unit, target, out var issuedCmd)) {
                    cooldowns[cooldownKey] = gameTimeSec;
                    if (issuedCmd != null) RememberIssued(unit, issuedCmd, cooldownKey, $"Rule {i} \"{rule.Name}\" ({source})");
                    if (slot.HasValue) slotUsed[(int)slot.Value] = true;
                    if (rule.Action.Type == ActionType.ToggleActivatable
                        && !string.IsNullOrEmpty(rule.Action.AbilityId)) {
                        togglesUsed.Add(rule.Action.AbilityId);
                    }
                    // Only gated (Standard) rules go into the tracker, so its contents keep
                    // exactly their present meaning and ActiveRuleTracker stays untouched.
                    if (issuedCmd != null && ActionSlots.IsGated(slot)) {
                        ActiveRuleTracker.Record(unit, source, entry.Id, issuedCmd);
                    }
                    Log.Engine.Info($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): EXECUTED [{SlotLabel(slot)}] -> {FormatTarget(target)}");

                    // DoNothing is the only hard stop: it means "this unit does nothing else
                    // this tick", which is exactly the pre-change semantics.
                    if (rule.Action.Type == ActionType.DoNothing) return true;
                }
            }
            return false;
        }

        // True when the given slot holds an unfinished ability command. Deliberately
        // restricted to UnitUseAbility: the Move slot is near-permanently occupied by
        // engine-issued UnitMoveTo (approach, formation), so a bare occupancy check would
        // mean a move-action ability never fires while the unit walks — the same over-block
        // regression already documented for PlayerCommandGuard. Source-agnostic on purpose:
        // it also skips when the ability command in that slot came from the player.
        static bool IsSlotBusyWithAbility(UnitEntityData unit, UnitCommand.CommandType slot) {
            var slots = unit.Commands?.Raw;
            if (slots == null) return false;
            int idx = (int)slot;
            if (idx < 0 || idx >= slots.Length) return false;
            return slots[idx] is UnitUseAbility cmd && !cmd.IsFinished;
        }

        // Scans every OTHER slot for an unfinished UnitUseAbility / UnitAttack and asks
        // ActionSlots.CheckConflict whether issuing into `issuing` may overlap it. Engine
        // inputs: HasCooldownForCommand(issuing) says whether our command would start this
        // frame; Cooldown.StandardAction is how long a pending Standard is still held back.
        // Same-slot occupants are skipped on purpose — Run replaces them (that IS priority
        // preemption for Standard) and IsSlotBusyWithAbility covers the non-Standard case.
        // Commands we issued that have not started yet, with the cooldown they stamped. A
        // command the engine parked in Commands.Queue can be wiped by ANY later Run() on
        // the unit (party AI re-issuing its default action, a player click) before it ever
        // starts — RunVerified had no way to know. When that happens the rule cooldown was
        // burnt for nothing; refund it so the rule retries on the next tick instead of
        // sitting out a round (deck 2026-09-09: Evil Eye queued behind a casting Ray of
        // Frost vanished 7×, landed 1×).
        struct IssuedCommand {
            public UnitEntityData Unit;
            public UnitCommand Command;
            public (string, string) CooldownKey;
            public string Label;
        }
        static readonly List<IssuedCommand> issued = new List<IssuedCommand>();

        static void RememberIssued(UnitEntityData unit, UnitCommand cmd, (string, string) cooldownKey, string label) {
            issued.Add(new IssuedCommand { Unit = unit, Command = cmd, CooldownKey = cooldownKey, Label = label });
        }

        static void RefundVanishedCommands(UnitEntityData unit) {
            for (int i = issued.Count - 1; i >= 0; i--) {
                var e = issued[i];
                if (!ReferenceEquals(e.Unit, unit)) continue;
                var cmd = e.Command;
                if (cmd.IsStarted || cmd.IsFinished) {
                    issued.RemoveAt(i);
                    continue;
                }
                var commands = unit.Commands;
                if (commands != null && commands.ContainsOrQueued(cmd)) continue;
                issued.RemoveAt(i);
                if (cooldowns.Remove(e.CooldownKey))
                    Log.Engine.Info($"{unit.CharacterName} {e.Label}: command vanished before starting — cooldown refunded");
            }
        }

        static bool HasCrossSlotConflict(UnitEntityData unit, UnitCommand.CommandType issuing, out string reason) {
            reason = null;
            // Our own command parked in Commands.Queue: the engine will run it when the slot
            // frees, but ANY Run() we do now clears that queue first (m_Queue.Clear() in
            // UnitCommands.Run) — including a Move rule "overlapping" it. Wait.
            var queue = unit.Commands?.Queue;
            if (queue != null && queue.Count > 0) {
                foreach (var queued in queue) {
                    if (queued == null || !PlayerCommandGuard.IsOurs(unit, queued)) continue;
                    string queuedWhat = queued is UnitUseAbility qa ? (qa.Ability?.Name ?? "ability") : "attack";
                    reason = $"own {queuedWhat} queued in {queued.Type} (any new command would clear the queue)";
                    return true;
                }
            }
            var slots = unit.Commands?.Raw;
            if (slots == null) return false;

            var combat = unit.CombatState;
            bool issuingOnCooldown = combat != null && combat.HasCooldownForCommand(issuing);
            float standardRemaining = combat?.Cooldown?.StandardAction ?? 0f;

            for (int i = 0; i < slots.Length; i++) {
                if (i == (int)issuing) continue;
                var cmd = slots[i];
                if (cmd == null || cmd.IsFinished) continue;
                if (!(cmd is UnitUseAbility) && !(cmd is UnitAttack)) continue;

                var occupied = (UnitCommand.CommandType)i;
                bool approaching = !cmd.IsStarted && !cmd.IsUnitCloseEnough();
                bool own = PlayerCommandGuard.IsOurs(unit, cmd);
                var verdict = ActionSlots.CheckConflict(issuing, occupied, cmd.IsStarted, approaching, own, issuingOnCooldown, standardRemaining);
                string what = (own ? "own " : "foreign ") + (cmd is UnitUseAbility ua ? (ua.Ability?.Name ?? "ability") : "attack");
                switch (verdict) {
                    case SlotConflict.Running:
                        reason = $"{what} running in {occupied}";
                        return true;
                    case SlotConflict.Approaching:
                        reason = $"{what} in {occupied} still approaching its target";
                        return true;
                    case SlotConflict.PairedOwn:
                        reason = $"{what} {(cmd.IsStarted ? "running" : "pending")} in {occupied} — a {issuing} command would cancel it (paired slot)";
                        return true;
                    case SlotConflict.Pending:
                        reason = $"{what} pending in {occupied} (Standard cooldown {standardRemaining:F1}s, {issuing} on cooldown={issuingOnCooldown})";
                        return true;
                    default:
                        if (issuing == UnitCommand.CommandType.Move && occupied == UnitCommand.CommandType.Standard)
                            Log.Engine.Trace($"  {unit.CharacterName}: Move will cancel {what} in Standard (engine command, re-issued by the party AI)");
                        else
                            Log.Engine.Trace($"  {unit.CharacterName}: {issuing} may overlap pending {what} in {occupied} — engine holds Standard for {standardRemaining:F1}s");
                        break;
                }
            }
            return false;
        }

        static string SlotLabel(UnitCommand.CommandType? slot) {
            return slot.HasValue ? slot.Value.ToString() : "no-slot";
        }

        public static float GetCombatRoundsElapsed(float gameTimeSec) {
            if (!wasInCombat) return 0;
            return (gameTimeSec - combatStartTime) / 6f;
        }

        public static void Reset() {
            lastTickTime = 0;
            combatStartTime = 0;
            wasInCombat = false;
            forceNextTick = false;
            tickCounter = 0;
            cooldowns.Clear();
            issued.Clear();
            ActiveRuleTracker.Reset();
        }

        static string FormatTarget(ResolvedTarget target) {
            if (target.Unit != null) return target.Unit.CharacterName;
            if (target.Point.HasValue) {
                var p = target.Point.Value;
                return $"point({p.x:F1},{p.y:F1},{p.z:F1})";
            }
            return "self";
        }
    }
}
