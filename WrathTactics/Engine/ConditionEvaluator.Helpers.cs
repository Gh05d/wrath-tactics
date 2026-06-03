using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using WrathTactics.Logging;
using KmAlignment = Kingmaker.Enums.Alignment;

namespace WrathTactics.Engine {
    public static partial class ConditionEvaluator {
        internal static bool CheckCreatureType(UnitEntityData unit, string typeValue) {
            if (string.IsNullOrEmpty(typeValue)) return false;
            string target = typeValue.ToLowerInvariant();

            // Check Blueprint.Type (unit type blueprint)
            string bpTypeName = unit.Blueprint.Type?.name?.ToLowerInvariant() ?? "";
            if (bpTypeName.Contains(target)) {
                Log.Engine.Trace($"CreatureType matched on {unit.CharacterName} via Blueprint.Type: '{bpTypeName}'");
                return true;
            }

            // Check all features on the unit — creature types are typically features
            // named "UndeadType", "AnimalType", "ConstructType", etc.
            var progression = unit.Descriptor.Progression;
            if (progression?.Features != null) {
                foreach (var fact in progression.Features.Enumerable) {
                    var fname = fact?.Blueprint?.name?.ToLowerInvariant() ?? "";
                    if (fname.Contains(target)) {
                        Log.Engine.Trace($"CreatureType matched on {unit.CharacterName} via Feature: '{fname}'");
                        return true;
                    }
                }
            }

            // Also check all raw facts on the descriptor
            foreach (var fact in unit.Descriptor.Facts.List) {
                var fname = fact?.Blueprint?.name?.ToLowerInvariant() ?? "";
                if (fname.Contains(target)) {
                    Log.Engine.Trace($"CreatureType matched on {unit.CharacterName} via Fact: '{fname}'");
                    return true;
                }
            }

            Log.Engine.Trace($"CreatureType NO MATCH for {unit.CharacterName} (Blueprint.Type='{bpTypeName}', looking for '{target}')");
            return false;
        }

        static bool CheckAlignment(UnitEntityData unit, string component) {
            if (string.IsNullOrEmpty(component)) return false;
            var align = unit.Descriptor.Alignment.ValueRaw;
            switch (component.ToLowerInvariant()) {
                case "good":
                    return align == KmAlignment.LawfulGood
                        || align == KmAlignment.NeutralGood
                        || align == KmAlignment.ChaoticGood;
                case "evil":
                    return align == KmAlignment.LawfulEvil
                        || align == KmAlignment.NeutralEvil
                        || align == KmAlignment.ChaoticEvil;
                case "lawful":
                    return align == KmAlignment.LawfulGood
                        || align == KmAlignment.LawfulNeutral
                        || align == KmAlignment.LawfulEvil;
                case "chaotic":
                    return align == KmAlignment.ChaoticGood
                        || align == KmAlignment.ChaoticNeutral
                        || align == KmAlignment.ChaoticEvil;
                case "neutral":
                    // "Weder Good noch Evil": matches LN / TN / CN. Unaligned creatures
                    // (default = TrueNeutral) also match Neutral here — consistent with
                    // Pathfinder Detect Evil semantics (they don't match Good or Evil).
                    return align == KmAlignment.LawfulNeutral
                        || align == KmAlignment.TrueNeutral
                        || align == KmAlignment.ChaoticNeutral;
                default:
                    return false;
            }
        }

        public static bool HasConditionByName(UnitEntityData unit, string conditionName) {
            switch (conditionName?.ToLowerInvariant()) {
                case "paralyzed":  return unit.State.HasCondition(UnitCondition.Paralyzed);
                case "stunned":    return unit.State.HasCondition(UnitCondition.Stunned);
                case "frightened": return unit.State.HasCondition(UnitCondition.Frightened);
                case "nauseated":  return unit.State.HasCondition(UnitCondition.Nauseated);
                case "confused":   return unit.State.HasCondition(UnitCondition.Confusion);
                case "blinded":    return unit.State.HasCondition(UnitCondition.Blindness);
                case "prone":      return unit.State.HasCondition(UnitCondition.Prone);
                case "entangled":  return unit.State.HasCondition(UnitCondition.Entangled);
                case "exhausted":  return unit.State.HasCondition(UnitCondition.Exhausted);
                case "fatigued":   return unit.State.HasCondition(UnitCondition.Fatigued);
                case "shaken":     return unit.State.HasCondition(UnitCondition.Shaken);
                case "sickened":   return unit.State.HasCondition(UnitCondition.Sickened);
                case "sleeping":   return unit.State.HasCondition(UnitCondition.Sleeping);
                case "petrified":  return unit.State.HasCondition(UnitCondition.Petrified);
                case "slowed":     return unit.State.HasCondition(UnitCondition.Slowed);
                case "staggered":  return unit.State.HasCondition(UnitCondition.Staggered);
                case "dazed":      return unit.State.HasCondition(UnitCondition.Dazed);
                case "dazzled":    return unit.State.HasCondition(UnitCondition.Dazzled);
                case "helpless":   return unit.State.HasCondition(UnitCondition.Helpless);
                case "cowering":   return unit.State.HasCondition(UnitCondition.Cowering);
                case "deathdoor":  return unit.State.HasCondition(UnitCondition.DeathDoor);
                default:           return false;
            }
        }

        // True if the unit carries any active buff flagged with the given SpellDescriptor
        // (Poison/Disease/Bleed). One check catches every poison/disease buff — these
        // statuses are descriptor-flagged buffs, not UnitConditions, which is why they
        // aren't in HasConditionByName. descriptorName must match the game SpellDescriptor
        // enum exactly.
        internal static bool HasBuffWithDescriptor(UnitEntityData unit, string descriptorName) {
            if (unit == null || string.IsNullOrEmpty(descriptorName)) return false;
            if (!System.Enum.TryParse<Kingmaker.Blueprints.Classes.Spells.SpellDescriptor>(descriptorName, out var want))
                return false;
            long wantBits = (long)want;
            if (wantBits == 0) return false;
            foreach (var buff in unit.Buffs.RawFacts) {
                if (((long)buff.Blueprint.SpellDescriptor & wantBits) != 0) return true;
            }
            return false;
        }

        // Engine-authoritative energy-immunity check via UnitPartDamageReduction.IsImmune
        // (verified IL). Covers the five castable elementals (Fire/Cold/Electricity/Acid/
        // Sonic). False when the unit has no damage-reduction part or the energy is unknown.
        internal static bool IsImmuneToEnergy(UnitEntityData unit, string energyName) {
            if (unit == null || string.IsNullOrEmpty(energyName)) return false;
            if (!System.Enum.TryParse<Kingmaker.Enums.Damage.DamageEnergyType>(energyName, out var energy))
                return false;
            var dr = unit.Get<Kingmaker.UnitLogic.Parts.UnitPartDamageReduction>();
            return dr != null && dr.IsImmune(energy);
        }

        static int CountAvailableSlotsAtLevel(UnitEntityData unit, int level) {
            int total = 0;
            foreach (var book in unit.Spellbooks) {
                if (book.Blueprint.Spontaneous) {
                    total += book.GetSpontaneousSlots(level);
                } else {
                    foreach (var slot in book.GetMemorizedSpells(level)) {
                        if (slot.Spell != null && slot.Available)
                            total++;
                    }
                }
            }
            return total;
        }

        static int CountAvailableSlotsAboveLevel(UnitEntityData unit, int minLevel) {
            // Use the highest MaxSpellLevel across the unit's spellbooks. Mythic
            // books cap at 10; hardcoding 9 silently dropped mythic level-10 slots.
            int maxLevel = 0;
            foreach (var book in unit.Spellbooks) {
                if (book.MaxSpellLevel > maxLevel) maxLevel = book.MaxSpellLevel;
            }
            int total = 0;
            for (int l = minLevel; l <= maxLevel; l++) {
                total += CountAvailableSlotsAtLevel(unit, l);
            }
            return total;
        }

        static bool HasResource(UnitEntityData unit, string resourceGuid) {
            if (string.IsNullOrEmpty(resourceGuid)) return false;
            foreach (var resource in unit.Resources.PersistantResources) {
                if (resource.Blueprint.AssetGuid.ToString() == resourceGuid && resource.Amount > 0)
                    return true;
            }
            return false;
        }
    }
}
