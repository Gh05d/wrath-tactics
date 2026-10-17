using Kingmaker.Blueprints;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.ActivatableAbilities;
using WrathTactics.Logging;

namespace WrathTactics.Engine {
    /// <summary>
    /// Finds a quickslotted metamagic rod on a unit that can apply to a given spell.
    /// Scans the unit's activatables for a rod toggle whose buff carries
    /// `MetamagicRodMechanics` and asks that component `IsSuitableAbility` (engine check:
    /// metamagic flag, MaxSpellLevel, spellbook spell, AvailableMetamagic).
    /// Not `UnitPartSpecialMetamagic`: the component is a buff component and registers there
    /// in `OnTurnOn`, i.e. only while the rod is already on — that list is empty for every
    /// rod we would need to switch on.
    /// Returns null on no match — callers fall back to a normal cast.
    /// </summary>
    public static class MetamagicRodResolver {
        /// <summary>
        /// First rod toggle on `unit` whose `Metamagic == metamagic`, whose
        /// `IsSuitableAbility(ability)` returns true and that is either already on or still
        /// has charges. Returns null if no rod is quickslotted, or none match the spell.
        /// </summary>
        public static ActivatableAbility TryResolve(UnitEntityData unit, AbilityData ability, Metamagic metamagic) {
            if (unit == null || ability == null) return null;
            foreach (var rod in unit.ActivatableAbilities.RawFacts) {
                var mech = rod.Blueprint.Buff?.GetComponent<MetamagicRodMechanics>();
                if (mech == null) continue;
                if (mech.Metamagic != metamagic) continue;
                if (!mech.IsSuitableAbility(ability)) continue;
                if (!rod.IsOn && !rod.IsAvailable) continue; // out of charges for today
                Log.Engine.Debug($"MetamagicRodResolver: matched {mech.Metamagic} rod {rod.Blueprint.name} for {ability.Name} on {unit.CharacterName}");
                return rod;
            }
            return null;
        }
    }
}
