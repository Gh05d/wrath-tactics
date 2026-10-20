using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.Abilities;
using Kingmaker.UnitLogic.Abilities.Blueprints;
using WrathTactics.Logging;

namespace WrathTactics.Engine {
    // One situational save bonus found on a target fact. Existing is the largest
    // positive modifier of the same descriptor already sitting on the target's save stat.
    internal struct SaveBonusEntry {
        public string Source;
        public int Descriptor;
        public bool Stackable;
        public int Amount;
        public int Existing;
    }

    // Pure arithmetic of the SpellDCMinusSave margin. Game-type walking lives in
    // SpellSaveBonusProbe; this part has no game dependency and is unit-tested.
    internal static class SpellDcMargin {
        // Net situational bonus after the game's stacking rule: stackable descriptors
        // add fully; non-stackable ones contribute only the largest new entry per
        // descriptor, and only the part that exceeds what the stat already carries.
        internal static int NetBonus(IEnumerable<SaveBonusEntry> entries) {
            if (entries == null) return 0;
            int total = 0;
            var nonStackable = new Dictionary<int, SaveBonusEntry>();
            foreach (var e in entries) {
                if (e.Stackable) {
                    total += e.Amount;
                    continue;
                }
                SaveBonusEntry best;
                if (!nonStackable.TryGetValue(e.Descriptor, out best) || e.Amount > best.Amount)
                    nonStackable[e.Descriptor] = e;
            }
            foreach (var best in nonStackable.Values)
                total += Math.Max(0, best.Amount - best.Existing);
            return total;
        }

        // margin = (DC + caster-side DC bonus) - (base save + situational target bonus).
        // Positive ⇒ target likely fails.
        internal static int Margin(int dc, int casterDcBonus, int baseSave, int situationalBonus) {
            return dc + casterDcBonus - baseSave - situationalBonus;
        }
    }

    // Walks the target's facts for the save bonuses the game adds inside
    // RuleSavingThrow (it cannot be queried without rolling). Covers the four
    // component types in the summary below; other types (specific spells, ability
    // type, school-ability-value) and Mythic halving are not modelled.
    internal static class SpellSaveBonusProbe {
        static bool _warned;

        internal struct Result {
            public int CasterDcBonus;
            public int SituationalBonus;
            public string Breakdown;
        }

        // Never throws: a failure logs once and degrades to "no situational bonus".
        internal static Result Probe(UnitEntityData caster, UnitEntityData target, AbilityData ability,
                                     SavingThrowType saveType) {
            try {
                return ProbeCore(caster, target, ability, saveType);
            } catch (Exception ex) {
                if (!_warned) {
                    _warned = true;
                    Log.Engine.Warn($"SpellDCMinusSave: situational save bonus probe failed, using base save only: {ex.Message}");
                }
                return new Result { Breakdown = "probe-failed" };
            }
        }

        static Result ProbeCore(UnitEntityData caster, UnitEntityData target, AbilityData ability,
                                SavingThrowType saveType) {
            var bp = ability.Blueprint;
            var descriptor = bp.SpellDescriptor;
            var school = ability.SpellSchool;
            var spellbook = ability.Spellbook;
            bool isSpell = bp.Type == AbilityType.Spell;
            var stat = GetStat(target, saveType);

            var entries = new List<SaveBonusEntry>();
            var sb = new StringBuilder();

            foreach (var fact in EnumerateFacts(target)) {
                if (!fact.IsActive) continue;
                var comps = fact.Blueprint?.ComponentsArray;
                if (comps == null) continue;
                foreach (var c in comps) {
                    var d = c as SavingThrowBonusAgainstDescriptor;
                    if (d != null) {
                        if (!HasAny(descriptor, d.SpellDescriptor)) continue;
                        int amount = d.Bonus.Calculate(fact.MaybeContext) + d.Value * fact.GetRank();
                        if (d.OnlyPositiveValue && amount <= d.Value) continue;
                        if (Disabled(target, d.DisablingFeature)) continue;
                        Add(entries, sb, stat, "AgainstDescriptor " + fact.Blueprint.name, d.ModifierDescriptor, amount);
                        continue;
                    }
                    var cd = c as SavingThrowContextBonusAgainstDescriptor;
                    if (cd != null) {
                        if (!HasAny(descriptor, cd.SpellDescriptor)) continue;
                        int amount = cd.Value.Calculate(fact.MaybeContext);
                        Add(entries, sb, stat, "ContextAgainstDescriptor " + fact.Blueprint.name, cd.ModifierDescriptor, amount);
                        continue;
                    }
                    var sc = c as SavingThrowBonusAgainstSchool;
                    if (sc != null) {
                        if (sc.School != school) continue;
                        Add(entries, sb, stat, "AgainstSchool " + fact.Blueprint.name, sc.ModifierDescriptor, sc.Value);
                        continue;
                    }
                    var st = c as SavingThrowBonusAgainstSpellType;
                    if (st != null) {
                        if (!isSpell || spellbook == null) continue;
                        if (spellbook.Blueprint.IsArcane != st.AgainstArcaneSpells) continue;
                        int amount = st.Bonus.Calculate(fact.MaybeContext) + st.Value * fact.GetRank();
                        if (st.OnlyPositiveValue && amount <= st.Value) continue;
                        Add(entries, sb, stat, "AgainstSpellType(" + (st.AgainstArcaneSpells ? "arcane" : "divine") + ") " + fact.Blueprint.name,
                            st.ModifierDescriptor, amount);
                    }
                }
            }

            int casterBonus = 0;
            foreach (var fact in EnumerateFacts(caster)) {
                if (!fact.IsActive) continue;
                var comps = fact.Blueprint?.ComponentsArray;
                if (comps == null) continue;
                foreach (var c in comps) {
                    var inc = c as IncreaseCastersSavingThrowTypeDC;
                    if (inc == null || inc.Type != saveType) continue;
                    casterBonus += inc.BonusDC;
                    sb.Append($" +DC{inc.BonusDC}({fact.Blueprint.name})");
                }
            }

            return new Result {
                CasterDcBonus = casterBonus,
                SituationalBonus = SpellDcMargin.NetBonus(entries),
                Breakdown = sb.ToString(),
            };
        }

        static void Add(List<SaveBonusEntry> entries, StringBuilder sb, ModifiableValue stat,
                        string source, ModifierDescriptor desc, int amount) {
            if (amount == 0) return;
            bool stackable = desc.IsStackable();
            int existing = 0;
            if (!stackable && stat != null) {
                foreach (var m in stat.Modifiers)
                    if (m.ModDescriptor == desc && m.ModValue > existing) existing = m.ModValue;
            }
            entries.Add(new SaveBonusEntry {
                Source = source, Descriptor = (int)desc, Stackable = stackable, Amount = amount, Existing = existing,
            });
            sb.Append($" +{amount}({source}, {desc}{(stackable ? "" : ", no-stack")})");
        }

        static bool HasAny(SpellDescriptor have, SpellDescriptorWrapper want) {
            return (have & (SpellDescriptor)want) != 0;
        }

        static bool Disabled(UnitEntityData unit, Kingmaker.Blueprints.Facts.BlueprintUnitFact feature) {
            return feature != null && unit.HasFact(feature);
        }

        static ModifiableValue GetStat(UnitEntityData unit, SavingThrowType type) {
            switch (type) {
                case SavingThrowType.Fortitude: return unit.Stats.SaveFortitude;
                case SavingThrowType.Reflex:    return unit.Stats.SaveReflex;
                case SavingThrowType.Will:      return unit.Stats.SaveWill;
                default:                        return null;
            }
        }

        // Facts list + buffs + progression features, de-duplicated by reference.
        static IEnumerable<EntityFact> EnumerateFacts(UnitEntityData unit) {
            var seen = new HashSet<EntityFact>();
            var d = unit.Descriptor;
            foreach (var f in d.Facts.List) if (f != null && seen.Add(f)) yield return f;
            foreach (var f in d.Buffs.Enumerable) if (f != null && seen.Add(f)) yield return f;
            var features = d.Progression?.Features;
            if (features != null)
                foreach (var f in features.Enumerable) if (f != null && seen.Add(f)) yield return f;
        }
    }
}
