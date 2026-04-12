using System.Linq;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.ActivatableAbilities;
using Kingmaker.Utility;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    public static class ActionValidator {
        public static bool CanExecute(ActionDef action, UnitEntityData owner, UnitEntityData target) {
            if (owner.Commands.IsRunning())
                return false;

            switch (action.Type) {
                case ActionType.CastSpell:
                    return CanCastSpell(action.AbilityId, owner, target);
                case ActionType.UseItem:
                    return CanUseItem(action.AbilityId, owner, target);
                case ActionType.ToggleActivatable:
                    return CanToggleActivatable(action.AbilityId, owner);
                case ActionType.AttackTarget:
                    return target != null && target.HPLeft > 0;
                case ActionType.DoNothing:
                    return true;
                default:
                    return false;
            }
        }

        static bool CanCastSpell(string abilityGuid, UnitEntityData owner, UnitEntityData target) {
            var ability = FindAbility(owner, abilityGuid);
            if (ability == null) return false;

            if (ability.Spellbook != null) {
                int level = ability.Spellbook.GetSpellLevel(ability);
                if (ability.Spellbook.GetSpellsPerDay(level) <= 0)
                    return false;
            }

            if (target != null && !ability.CanTarget(new TargetWrapper(target)))
                return false;

            return true;
        }

        static bool CanUseItem(string abilityGuid, UnitEntityData owner, UnitEntityData target) {
            var ability = FindAbilityFromItem(owner, abilityGuid);
            if (ability == null) return false;
            if (ability.SourceItem != null && ability.SourceItem.Charges <= 0) return false;
            if (target != null && !ability.CanTarget(new TargetWrapper(target)))
                return false;
            return true;
        }

        static bool CanToggleActivatable(string abilityGuid, UnitEntityData owner) {
            var activatable = FindActivatable(owner, abilityGuid);
            if (activatable == null) return false;
            return !activatable.IsOn && activatable.IsAvailable;
        }

        public static AbilityData FindAbility(UnitEntityData owner, string abilityGuid) {
            if (string.IsNullOrEmpty(abilityGuid)) return null;

            foreach (var book in owner.Spellbooks) {
                for (int level = 0; level <= 10; level++) {
                    foreach (var spell in book.GetKnownSpells(level)) {
                        if (spell.Blueprint.AssetGuid.ToString() == abilityGuid)
                            return spell;
                    }
                    foreach (var spell in book.GetCustomSpells(level)) {
                        if (spell.Blueprint.AssetGuid.ToString() == abilityGuid)
                            return spell;
                    }
                }
            }

            foreach (var ability in owner.Abilities.RawFacts) {
                if (ability.Blueprint.AssetGuid.ToString() == abilityGuid && ability.Data.SourceItem == null)
                    return ability.Data;
            }

            return null;
        }

        static AbilityData FindAbilityFromItem(UnitEntityData owner, string abilityGuid) {
            foreach (var ability in owner.Abilities.RawFacts) {
                if (ability.Blueprint.AssetGuid.ToString() == abilityGuid && ability.Data.SourceItem != null)
                    return ability.Data;
            }
            return null;
        }

        public static ActivatableAbility FindActivatable(UnitEntityData owner, string abilityGuid) {
            if (string.IsNullOrEmpty(abilityGuid)) return null;
            return owner.ActivatableAbilities.RawFacts
                .FirstOrDefault(a => a.Blueprint.AssetGuid.ToString() == abilityGuid);
        }
    }
}
