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

        public static bool HasForeignActiveCommand(UnitEntityData unit) {
            if (unit?.Commands == null) return false;

            issuedByUnit.TryGetValue(unit.UniqueId, out var ours);
            ours?.RemoveWhere(c => c == null || c.IsFinished);

            var std = unit.Commands.Standard;
            if (std == null || std.IsFinished) return false;
            if (!(std is UnitUseAbility useAbility)) return false;
            if (ours != null && ours.Contains(std)) return false;

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

            Log.Engine.Trace($"  Foreign cast detected on {unit.CharacterName}: {slotBp?.name ?? std.GetType().Name}");
            return true;
        }

        public static void Reset() {
            issuedByUnit.Clear();
        }
    }
}
