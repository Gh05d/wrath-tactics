using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Parts;
using WrathTactics.Logging;

namespace WrathTactics.Engine {
    /// <summary>
    /// Finds a metamagic rod on a unit that can apply to a given spell.
    /// Engine-authoritative: queries `UnitPartSpecialMetamagic` (the same engine-side
    /// list that vanilla UI uses) and `MetamagicRodMechanics.IsSuitableAbility`.
    /// Returns null on no match — callers fall back to a normal cast.
    /// </summary>
    public static class MetamagicRodResolver {
        /// <summary>
        /// First rod on `unit` whose `Metamagic == metamagic` and whose
        /// `IsSuitableAbility(ability)` returns true. Returns null if no rod is
        /// equipped+quickslotted, or none match the spell.
        /// The returned `MetamagicRodMechanics` carries `RodAbility` (the ActivatableAbility blueprint
        /// the caller activates).
        /// </summary>
        public static MetamagicRodMechanics TryResolve(UnitEntityData unit, AbilityData ability, Metamagic metamagic) {
            if (unit == null || ability == null) return null;
            var part = unit.Get<UnitPartSpecialMetamagic>();
            if (part == null) return null;
            // m_MetamagicRodMechanics is publicizer-accessible (private List<(EntityFact, MetamagicRodMechanics)>).
            var entries = part.m_MetamagicRodMechanics;
            if (entries == null || entries.Count == 0) return null;
            foreach (var entry in entries) {
                var mech = entry.Item2;
                if (mech == null) continue;
                if (mech.Metamagic != metamagic) continue;
                if (!mech.IsSuitableAbility(ability)) continue;
                Log.Engine.Debug($"MetamagicRodResolver: matched {mech.Metamagic} rod for {ability.Name} on {unit.CharacterName}");
                return mech;
            }
            return null;
        }
    }
}
