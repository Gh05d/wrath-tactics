using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;

namespace WrathTactics.Engine {
    public static class UnitExtensions {
        // HD matches the game's own ContextConditionHitDice check (IL-verified):
        // reads UnitProgressionData.CharacterLevel. Racial HD is already folded
        // into CharacterLevel for monsters. Mythic levels are NOT included,
        // matching vanilla HD-gated spells (Sleep, Color Spray, Hold Person).
        public static int GetHD(UnitEntityData unit) {
            var progression = unit?.Descriptor?.Progression;
            return progression?.CharacterLevel ?? 0;
        }

        // Looks up the target's modified save for the given save type. Returns 0
        // for SavingThrowType.Unknown — callers must pre-check the enum so "no
        // save" doesn't silently compare against 0.
        public static int GetSave(UnitEntityData unit, SavingThrowType type) {
            if (unit == null) return 0;
            switch (type) {
                case SavingThrowType.Fortitude: return unit.Stats.SaveFortitude.ModifiedValue;
                case SavingThrowType.Reflex:    return unit.Stats.SaveReflex.ModifiedValue;
                case SavingThrowType.Will:      return unit.Stats.SaveWill.ModifiedValue;
                default:                        return 0;
            }
        }
    }
}
