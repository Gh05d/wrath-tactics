using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic;
using WrathTactics.Logging;
using KmAlignment = Kingmaker.Enums.Alignment;

namespace WrathTactics.Engine {
    public static partial class ConditionEvaluator {
        // Exact blueprint GUIDs of the game's creature-type facts, keyed by the
        // lowercase dropdown key (EnumLabels.KeysForCreatureType). The engine has
        // no IsUndead-style API — its own targeting conditions (AbilityTargetHasFact /
        // ContextConditionHasFact) check these same type features, so GUID equality
        // is the authoritative test. Humanoid and Ooze have no type-fact blueprint;
        // they fall through to the exact-name rule only.
        static readonly System.Collections.Generic.Dictionary<string, string[]> TypeFactGuids =
            new System.Collections.Generic.Dictionary<string, string[]> {
                ["aberration"]        = new[] { "3bec99efd9a363242a6c8d9957b75e91" },
                ["animal"]            = new[] { "a95311b3dc996964cbaa30ff9965aaf6" },
                ["construct"]         = new[] { "fd389783027d63343b4a5634bd81645f" },
                ["dragon"]            = new[] { "455ac88e22f55804ab87c2467deff1d6" },
                ["fey"]               = new[] { "018af8005220ac94a9a4f47b3e9c2b4e" },
                ["magicalbeast"]      = new[] { "625827490ea69d84d8e599a33929fdc6" },
                ["monstroushumanoid"] = new[] { "57614b50e8d86b24395931fffc5e409b" },
                // PlantType + PlantTypeFake (units the game treats as plants for targeting)
                ["plant"]             = new[] { "706e61781d692a042b35941f14bc41c5", "b0efed5c0c814e3486fb8c8932af3bcc" },
                ["outsider"]          = new[] { "9054d3988d491d944ac144e27b6bc318" },
                ["undead"]            = new[] { "734a29b693e9ec346ba2951b27987e33" },
                ["vermin"]            = new[] { "09478937695300944a179530664e42ec" },
                ["incorporeal"]       = new[] { "c4a7f98d743bc784c9d4cf2105852c39" },
                // Swarms have no SwarmType — SwarmDiminutiveFeature + SwarmTinyFeature
                ["swarm"]             = new[] { "2e3e840ab458ce04c92064489f87ecc2", "5a04735fd0e952142bfc8ecf995e2361" },
            };

        // 'Humanoid' has no type fact of its own, so it cannot be matched positively.
        // The engine solves this by EXCLUSION: HoldPerson (Spells/Level3/HoldPerson.jbp)
        // and Daze (Spells/Level0/Daze.jbp) both define a legal humanoid target as one
        // carrying none of these facts. We mirror HoldPerson, the stricter of the two —
        // its extra SubtypeExtraplanar entry is what Daze's list omits, which is why the
        // game itself lets you Daze a 4-HD demon. Monsters pick their type fact up from
        // monster classes (AddClassLevels -> OutsiderClass / DragonClass / ...), so the
        // fact lands in Progression.Features at runtime — the same place we read.
        static readonly string[] NonHumanoidTypeKeys = {
            "aberration", "animal", "construct", "dragon", "fey", "magicalbeast",
            "monstroushumanoid", "outsider", "plant", "undead", "vermin", "swarm",
        };
        const string SubtypeExtraplanarGuid = "136fa0343d5b4b348bdaa05d83408db3";

        // True when this fact proves the unit is NOT a humanoid. Exact-match only,
        // same rule as IsCreatureTypeFactMatch — a substring match here would flip a
        // real humanoid to "not humanoid" on any incidentally named buff.
        internal static bool IsNonHumanoidFact(string factName, string factGuid) {
            foreach (var key in NonHumanoidTypeKeys)
                if (IsCreatureTypeFactMatch(key, factName, factGuid)) return true;
            return factGuid == SubtypeExtraplanarGuid || factName == "subtypeextraplanar";
        }

        // Substring matching is FORBIDDEN here: any buff whose name merely contains
        // the key turns a type check into a false positive (Nexus report 2026-07:
        // 'WrathOfTheUndeadCountBuff' — a hidden item fact on Iz Adamantine Golems —
        // made CreatureType=Undead match constructs). Exact GUID or exact name only.
        internal static bool IsCreatureTypeFactMatch(string target, string factName, string factGuid) {
            if (TypeFactGuids.TryGetValue(target, out var guids)) {
                foreach (var g in guids)
                    if (g == factGuid) return true;
            }
            return factName == target + "type" || factName == target;
        }

        internal static bool CheckCreatureType(UnitEntityData unit, string typeValue) {
            if (string.IsNullOrEmpty(typeValue)) return false;
            string target = typeValue.ToLowerInvariant();

            // Humanoid is absence-defined — the positive paths below can never match it.
            if (target == "humanoid") return IsHumanoidByExclusion(unit);

            // Blueprint.Type is the bestiary species ("Ghoul", "BlackDragon"), not the
            // category — kept as a curated-name fallback (e.g. "dragon" in "BlackDragon").
            string bpTypeName = unit.Blueprint.Type?.name?.ToLowerInvariant() ?? "";
            if (bpTypeName.Contains(target)) {
                Log.Engine.Trace($"CreatureType matched on {unit.CharacterName} via Blueprint.Type: '{bpTypeName}'");
                return true;
            }

            var progression = unit.Descriptor.Progression;
            if (progression?.Features != null) {
                foreach (var fact in progression.Features.Enumerable) {
                    var bp = fact?.Blueprint;
                    if (bp == null) continue;
                    if (IsCreatureTypeFactMatch(target, bp.name?.ToLowerInvariant() ?? "", bp.AssetGuid.ToString())) {
                        Log.Engine.Trace($"CreatureType matched on {unit.CharacterName} via Feature: '{bp.name}'");
                        return true;
                    }
                }
            }

            foreach (var fact in unit.Descriptor.Facts.List) {
                var bp = fact?.Blueprint;
                if (bp == null) continue;
                if (IsCreatureTypeFactMatch(target, bp.name?.ToLowerInvariant() ?? "", bp.AssetGuid.ToString())) {
                    Log.Engine.Trace($"CreatureType matched on {unit.CharacterName} via Fact: '{bp.name}'");
                    return true;
                }
            }

            Log.Engine.Trace($"CreatureType NO MATCH for {unit.CharacterName} (Blueprint.Type='{bpTypeName}', looking for '{target}')");
            return false;
        }

        // Humanoid := carries no fact that marks another creature type. Scans the same
        // two fact collections as CheckCreatureType, so a type granted by a monster class
        // (Progression.Features) counts just as much as one added straight to the unit.
        // Known hole: oozes have no type fact anywhere in the game data, so they read as
        // humanoid — the engine's own humanoid-only spells share that hole.
        static bool IsHumanoidByExclusion(UnitEntityData unit) {
            var progression = unit.Descriptor?.Progression;
            if (progression?.Features != null) {
                foreach (var fact in progression.Features.Enumerable) {
                    var bp = fact?.Blueprint;
                    if (bp == null) continue;
                    if (IsNonHumanoidFact(bp.name?.ToLowerInvariant() ?? "", bp.AssetGuid.ToString())) {
                        Log.Engine.Trace($"CreatureType Humanoid rejected on {unit.CharacterName} via Feature: '{bp.name}'");
                        return false;
                    }
                }
            }

            foreach (var fact in unit.Descriptor.Facts.List) {
                var bp = fact?.Blueprint;
                if (bp == null) continue;
                if (IsNonHumanoidFact(bp.name?.ToLowerInvariant() ?? "", bp.AssetGuid.ToString())) {
                    Log.Engine.Trace($"CreatureType Humanoid rejected on {unit.CharacterName} via Fact: '{bp.name}'");
                    return false;
                }
            }

            Log.Engine.Trace($"CreatureType Humanoid matched on {unit.CharacterName} (no non-humanoid type fact)");
            return true;
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

        // True if any of the six ability scores currently carries temporary Damage — the
        // condition (Lesser) Restoration heals. ModifiableValueAttributeStat.Damage is the
        // engine-tracked ability-damage counter (separate from .Drain, which only full
        // Restoration clears and which we deliberately do NOT count here). Stats are always
        // constructed for a unit with a Descriptor, but guard defensively — this runs on
        // every party + visible unit each eval tick.
        internal static bool HasAbilityDamage(UnitEntityData unit) {
            var s = unit?.Stats;
            if (s == null) return false;
            return s.Strength.Damage > 0
                || s.Dexterity.Damage > 0
                || s.Constitution.Damage > 0
                || s.Intelligence.Damage > 0
                || s.Wisdom.Damage > 0
                || s.Charisma.Damage > 0;
        }

        // True if the unit carries any negative levels (energy drain) — the condition
        // Restoration removes. UnitPartNegativeLevels.Count sums every drain entry,
        // temporary AND permanent alike (verified IL: get_Count sums Data.Count over
        // m_LevelsData with no EnergyDrainType filter). The part is absent on units
        // that were never drained, hence the null guard.
        internal static bool HasNegativeLevels(UnitEntityData unit) {
            var part = unit?.Get<Kingmaker.UnitLogic.Parts.UnitPartNegativeLevels>();
            return part != null && part.Count > 0;
        }

        // True if the unit currently fights with a ranged weapon. GetFirstWeapon is the
        // engine's canonical "what does this unit attack with" probe (verified IL: primary
        // hand unless unarmed, else secondary hand, else first armed natural limb — CanBeNull,
        // so unarmed/natural-only units yield null → false). Blueprint.IsRanged delegates to
        // WeaponType.AttackType ∈ {Ranged, RangedTouch}. Live check of the CURRENT weapon
        // set — an enemy that swaps to a melee set stops matching on the next tick.
        internal static bool WieldsRangedWeapon(UnitEntityData unit) {
            return unit?.GetFirstWeapon()?.Blueprint?.IsRanged == true;
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
