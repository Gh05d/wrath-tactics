using System;
using Kingmaker.Blueprints;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.RuleSystem;
using Kingmaker.RuleSystem.Rules.Abilities;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using Kingmaker.UnitLogic.Buffs.Blueprints;
using Kingmaker.Utility;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>
    /// Arcanist Arcane Reservoir boost for CastSpell rules. The reservoir is one base ability
    /// (free action, personal) with a Caster Level and a DC variant, each costing one point and
    /// applying a buff that the next cast from an arcanist spellbook consumes
    /// (`AddAbilityUseTrigger` → `ContextActionRemoveSelf`). While a boost buff is up the
    /// variants are engine-unavailable (`AbilityCasterHasNoFacts`), so a boost never stacks or
    /// burns a second point.
    /// Applied instantly with `RuleCastSpell` + `Spend()` — the pair `UnitUseAbility.OnAction`
    /// runs — instead of a second command: a separate free-action command can be dropped or
    /// reordered against the spell's own command (two-rule setup looped, Nexus 2026-10-13).
    /// </summary>
    public static class ArcaneReservoir {
        const string BaseAbilityGuid = "91295893ae9fdfb4b8936a93eff019df";
        const string CasterLevelVariantGuid = "0b10e8506f321f54eb4a4b39dd18d959";
        const string DCVariantGuid = "36f81ca4bf1066e48a96866a32329c19";
        // ArcanistArcaneReservoirCLBuff: its AddCasterLevelForSpellbook lists the spellbooks the
        // boost applies to (the DC buff lists the same five).
        const string CasterLevelBuffGuid = "33e0c3a2a54c0e7489fa4ec4d79a581b";

        public static bool HasReservoir(UnitEntityData unit) => FindBase(unit) != null;

        /// <summary>
        /// Spends one reservoir point on `boost` for the upcoming cast of `spell`. No-op (false)
        /// for None, for spells outside the arcanist spellbooks, and when the variant is
        /// engine-unavailable (empty reservoir, boost already pending).
        /// </summary>
        public static bool TryApply(UnitEntityData owner, AbilityData spell, ReservoirBoost boost) {
            if (boost == ReservoirBoost.None || owner == null || spell == null) return false;
            if (!BoostsSpellbook(spell.Spellbook)) {
                Log.Engine.Debug($"Arcane Reservoir: {spell.Name} is not from an arcanist spellbook, no boost for {owner.CharacterName}");
                return false;
            }
            return Apply(owner, boost, spell.Name);
        }

        // Separate from TryApply so the bridge can drive it on a unit without an arcanist book.
        public static bool Apply(UnitEntityData owner, ReservoirBoost boost, string forSpell = null) {
            var baseFact = FindBase(owner);
            if (baseFact == null) return false;
            var variantBp = ResourcesLibrary.TryGetBlueprint<BlueprintAbility>(
                boost == ReservoirBoost.CasterLevel ? CasterLevelVariantGuid : DCVariantGuid);
            if (variantBp == null) return false;
            var data = new AbilityData(baseFact.Data, variantBp);
            if (!data.IsAvailable) {
                Log.Engine.Debug($"Arcane Reservoir {boost} unavailable for {owner.CharacterName} ({data.GetUnavailableReason()})");
                return false;
            }
            try {
                Rulebook.Trigger(new RuleCastSpell(data, new TargetWrapper(owner)));
            } catch (Exception ex) {
                Log.Engine.Error(ex, $"Arcane Reservoir {boost} trigger failed for {owner.CharacterName}");
                return false;
            }
            data.Spend();
            Log.Engine.Info($"Arcane Reservoir {boost} spent by {owner.CharacterName}{(forSpell != null ? $" for {forSpell}" : "")}");
            return true;
        }

        static Ability FindBase(UnitEntityData unit) {
            if (unit == null) return null;
            foreach (var fact in unit.Abilities.RawFacts) {
                if (fact.Blueprint.AssetGuid.ToString() == BaseAbilityGuid) return fact;
            }
            return null;
        }

        static bool BoostsSpellbook(Spellbook book) {
            if (book == null) return false;
            var comp = ResourcesLibrary.TryGetBlueprint<BlueprintBuff>(CasterLevelBuffGuid)
                ?.GetComponent<AddCasterLevelForSpellbook>();
            if (comp == null) return false;
            foreach (var spellbook in comp.Spellbooks) {
                if (spellbook == book.Blueprint) return true;
            }
            return false;
        }
    }
}
