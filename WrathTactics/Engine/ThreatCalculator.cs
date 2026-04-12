using Kingmaker.EntitySystem.Entities;

namespace WrathTactics.Engine {
    public static class ThreatCalculator {
        public static float Calculate(UnitEntityData unit) {
            var stats = unit.Stats;
            int attackBonus = stats.BaseAttackBonus.ModifiedValue;
            int strMod = (stats.Strength.ModifiedValue - 10) / 2;
            int dexMod = (stats.Dexterity.ModifiedValue - 10) / 2;
            int damageMod = System.Math.Max(strMod, dexMod);
            int hd = unit.Progression.CharacterLevel;
            float avgDamage = hd + damageMod;
            float critFactor = 1.05f;
            return attackBonus + (avgDamage * critFactor);
        }
    }
}
