using System.Linq;
using Kingmaker.EntitySystem.Entities;

namespace WrathTactics.Engine {
    /// <summary>
    /// Detects active hostile-targeting relations between two units.
    /// Used by the IsTargeting* / IsTargetedBy* condition properties.
    ///
    /// Combines two engine signals because each alone misses cases:
    ///   - Commands.Standard.TargetUnit catches casters / archers / movers
    ///     whose active command points at the victim.
    ///   - CombatState.EngagedUnits catches melee-locked pairs in between
    ///     attack-frames where the active command is briefly not a UnitAttack.
    ///
    /// Approach-phase units (running toward but not yet swinging or engaged)
    /// match neither and are intentionally out of scope — see spec.
    /// </summary>
    internal static class TargetingRelations {
        public static bool Has(UnitEntityData attacker, UnitEntityData victim) {
            if (attacker == null || victim == null || attacker == victim)
                return false;

            var cmdTarget = attacker.Commands?.Standard?.TargetUnit;
            if (cmdTarget == victim) return true;

            var engaged = attacker.CombatState?.EngagedUnits;
            if (engaged != null && engaged.Contains(victim)) return true;

            return false;
        }
    }
}
