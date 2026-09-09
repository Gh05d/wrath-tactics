using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using WrathTactics.Logging;

namespace WrathTactics.Engine {
    // Detects player- (or other-mod-) issued spell casts so the tactics tick does not
    // override them. The engine has no source-flag on UnitCommand, so we identify our
    // own commands by reference: every Commands.Run() the mod issues is registered
    // here.
    //
    // Scope is intentionally narrow: Standard slot only, UnitUseAbility commands only.
    // The engine constantly fills Standard with auto-issued UnitAttack (auto-engage)
    // and Move with UnitMoveTo (approach / formation) for any companion without an
    // explicit order — checking those slots/types caused tactics to be permanently
    // blocked for any engaged unit (Ember stuck at 1/76 HP unable to heal because
    // the auto-attack slot looked "foreign"). Spell casts (UnitUseAbility) are never
    // engine-auto-issued, so they're a reliable signal of player intent — and the
    // original Nexus complaint was specifically about casts ("cast or do something
    // else ... midcast").
    public static class PlayerCommandGuard {
        static readonly Dictionary<string, HashSet<UnitCommand>> issuedByUnit
            = new Dictionary<string, HashSet<UnitCommand>>();

        public static void Track(UnitEntityData unit, UnitCommand cmd) {
            if (unit == null || cmd == null) return;
            if (!issuedByUnit.TryGetValue(unit.UniqueId, out var set)) {
                set = new HashSet<UnitCommand>();
                issuedByUnit[unit.UniqueId] = set;
            }
            set.Add(cmd);
        }

        /// <summary>True iff <paramref name="cmd"/> is a command this mod issued for <paramref name="unit"/>.</summary>
        public static bool IsOurs(UnitEntityData unit, UnitCommand cmd) {
            if (unit == null || cmd == null) return false;
            return issuedByUnit.TryGetValue(unit.UniqueId, out var set) && set.Contains(cmd);
        }

        public static bool HasForeignActiveCommand(UnitEntityData unit) {
            if (unit?.Commands == null) return false;

            issuedByUnit.TryGetValue(unit.UniqueId, out var ours);
            // Purge finished commands — and say how they ended. UnitCommand.Result is the
            // only place the engine records whether a command we issued actually acted
            // (Success) or got cut down (Interrupt / Fail). Without this line the v1.29.0
            // animation-collision regression was invisible in our own log: EXECUTED lines
            // looked healthy while the casts never landed.
            ours?.RemoveWhere(c => {
                if (c == null) return true;
                string what = c is UnitUseAbility ua ? (ua.Ability?.Name ?? c.GetType().Name) : c.GetType().Name;
                if (!c.IsFinished) {
                    // A command we issued can also disappear WITHOUT finishing: RunVerified
                    // accepts commands the engine parked in Commands.Queue, and any later
                    // Run() on the unit (player click, engine auto-attack, our own next
                    // rule) clears that queue. Such a command never starts and never
                    // finishes — drop it, or the entry lingers for the whole combat.
                    if (!c.IsStarted && !unit.Commands.ContainsOrQueued(c)) {
                        Log.Engine.Info($"{unit.CharacterName}: own {what} [{c.Type}] vanished before starting (slot/queue cleared)");
                        return true;
                    }
                    return false;
                }
                if (c.Result == UnitCommand.ResultType.Success) {
                    Log.Engine.Debug($"{unit.CharacterName}: own {what} [{c.Type}] ended: Success");
                } else {
                    Log.Engine.Info($"{unit.CharacterName}: own {what} [{c.Type}] ended: {c.Result}");
                }
                return true;
            });

            var std = unit.Commands.Standard;
            if (IsForeignCast(unit, std, ours)) {
                Log.Engine.Trace($"  Foreign cast detected on {unit.CharacterName}: {((UnitUseAbility)std).Ability?.Blueprint?.name ?? std.GetType().Name}");
                return true;
            }
            // A player click that arrives while the unit is busy (running cast, or an
            // auto-attack on the same target) does not land in a slot — UnitCommands.Run
            // parks it in Commands.Queue (TryAddToQueueInsteadOfRunImmediately) and the
            // engine runs it once the slot frees. Every Run() without doNotClearQueue
            // wipes that queue first, so if we issued anything now the player's order
            // would silently vanish. Stand down until the queue has drained.
            var queue = unit.Commands.Queue;
            if (queue != null && queue.Count > 0) {
                foreach (var queued in queue) {
                    if (IsForeignCast(unit, queued, ours)) {
                        Log.Engine.Trace($"  Foreign queued cast detected on {unit.CharacterName}: {((UnitUseAbility)queued).Ability?.Blueprint?.name ?? queued.GetType().Name}");
                        return true;
                    }
                }
            }
            return false;
        }

        // Standard-slot / queued UnitUseAbility that is neither ours nor the unit's
        // AutoUseAbility (see the comment block below for why that one is exempt).
        static bool IsForeignCast(UnitEntityData unit, UnitCommand cmd, HashSet<UnitCommand> ours) {
            if (cmd == null || cmd.IsFinished) return false;
            if (cmd.Type != UnitCommand.CommandType.Standard) return false;
            if (!(cmd is UnitUseAbility useAbility)) return false;
            if (ours != null && ours.Contains(cmd)) return false;

            // Right-click "default action" (UnitBrain.AutoUseAbility) goes through the
            // same Commands.Run pipeline as a manual click — same UnitUseAbility class,
            // no source flag. Distinguish by blueprint: if the in-slot ability matches
            // the unit's configured AutoUseAbility, it's an engine default-action firing
            // on cooldown (not a fresh player click), and tactics is allowed to preempt
            // it. Without this, e.g. Ember with Magic Missile as her default action
            // would be permanently blocked from tactics rules for the entire combat
            // because the engine re-issues Magic Missile every cooldown.
            var slotBp = useAbility.Ability?.Blueprint;
            var autoBp = unit.Brain?.AutoUseAbility?.Blueprint;
            if (slotBp != null && autoBp != null && slotBp == autoBp) {
                return false;
            }

            return true;
        }

        public static void Reset() {
            issuedByUnit.Clear();
        }
    }
}
