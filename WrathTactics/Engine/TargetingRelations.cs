using System;
using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;

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
        /// <param name="attacker">The unit whose active command and engagement state are inspected.</param>
        /// <param name="victim">The unit being tested as the target of that command/engagement.</param>
        public static bool Has(UnitEntityData attacker, UnitEntityData victim) {
            if (attacker == null || victim == null || attacker == victim)
                return false;

            var cmdTarget = attacker.Commands?.Standard?.TargetUnit;
            if (cmdTarget == victim) return true;

            var engaged = attacker.CombatState?.EngagedUnits;
            if (engaged != null && engaged.Contains(victim)) return true;

            return false;
        }

        /// <summary>
        /// True when <paramref name="caster"/> is casting an ability at <paramref name="victim"/>
        /// right now: a <c>UnitUseAbility</c> in the Standard slot or still parked in
        /// <c>Commands.Queue</c> (the engine queues a click behind a running cast). Unlike
        /// <see cref="Has"/> this ignores melee engagement and plain attacks, so a sword swing
        /// never counts as "being cast on". Used by IsBeingCastOnByAlly.
        /// </summary>
        /// <param name="abilityGuid">Blueprint GUID filter; empty = any ability.</param>
        public static bool HasCast(UnitEntityData caster, UnitEntityData victim, string abilityGuid) {
            if (caster == null || victim == null || caster == victim)
                return false;

            var commands = caster.Commands;
            if (commands == null) return false;
            if (IsCastOn(commands.Standard, victim, abilityGuid)) return true;

            var queue = commands.Queue;
            if (queue == null) return false;
            foreach (var queued in queue) {
                if (IsCastOn(queued, victim, abilityGuid)) return true;
            }
            return false;
        }

        static bool IsCastOn(UnitCommand command, UnitEntityData victim, string abilityGuid) {
            if (command == null || command.IsFinished) return false;
            if (!(command is UnitUseAbility cast) || cast.TargetUnit != victim) return false;
            var ability = cast.Ability;
            return MatchesAbilityFilter(abilityGuid,
                ability?.Blueprint?.AssetGuid.ToString(),
                ability?.ConvertedFrom?.Blueprint?.AssetGuid.ToString());
        }

        /// <summary>
        /// Pure ability filter: an empty filter matches any cast; otherwise the filter must equal
        /// the cast blueprint's GUID or the GUID of the ability it was converted from (spell
        /// variants, spontaneous conversion). Exact match only, never Contains.
        /// </summary>
        public static bool MatchesAbilityFilter(string filter, string castGuid, string convertedFromGuid) {
            if (string.IsNullOrEmpty(filter)) return true;
            return string.Equals(filter, castGuid, StringComparison.OrdinalIgnoreCase)
                || string.Equals(filter, convertedFromGuid, StringComparison.OrdinalIgnoreCase);
        }
    }
}
