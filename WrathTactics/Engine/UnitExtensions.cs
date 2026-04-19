using Kingmaker.EntitySystem.Entities;

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
    }
}
