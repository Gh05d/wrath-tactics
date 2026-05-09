using System.Linq;
using Kingmaker.Blueprints;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.ActivatableAbilities;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    public static partial class ActionValidator {
        static bool CanToggleActivatable(string abilityGuid, UnitEntityData owner, ToggleMode mode) {
            var activatable = FindActivatable(owner, abilityGuid);
            if (activatable == null) return false;
            if (mode == ToggleMode.Off)
                return activatable.IsOn;
            return !activatable.IsOn && activatable.IsAvailable;
        }

        public static ActivatableAbility FindActivatable(UnitEntityData owner, string abilityGuid) {
            if (string.IsNullOrEmpty(abilityGuid)) return null;
            return owner.ActivatableAbilities.RawFacts
                .FirstOrDefault(a => a.Blueprint.AssetGuid.ToString() == abilityGuid);
        }
    }
}
