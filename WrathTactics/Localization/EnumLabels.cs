using System;
using System.Collections.Generic;
using Kingmaker.UnitLogic.Abilities;
using WrathTactics.Models;

namespace WrathTactics.Localization {
    public static class EnumLabels {
        public static string For(ConditionSubject v) => $"enum.subject.{v}".i18n();
        public static string For(ConditionProperty v) => $"enum.property.{v}".i18n();
        public static string For(ActionType v) => $"enum.action.{v}".i18n();
        public static string For(TargetType v) => $"enum.target.{v}".i18n();
        public static string For(HealMode v) => $"enum.heal_mode.{v}".i18n();
        public static string For(ToggleMode v) => $"enum.toggle_mode.{v}".i18n();
        public static string For(ThrowSplashMode v) => $"enum.splash_mode.{v}".i18n();
        public static string For(RangeBracket v) => $"enum.range.{v}".i18n();
        public static string For(HealEnergyType v) => $"enum.heal_energy.{v}".i18n();

        public static List<string> NamesFor<T>() where T : Enum {
            var values = (T[])Enum.GetValues(typeof(T));
            var key = TypeKey(typeof(T));
            var result = new List<string>(values.Length);
            foreach (var v in values) result.Add($"enum.{key}.{v}".i18n());
            return result;
        }

        public static List<string> KeysForCreatureType() => new List<string> {
            "Aberration", "Animal", "Construct", "Dragon", "Fey",
            "Humanoid", "MagicalBeast", "MonstrousHumanoid", "Ooze",
            "Outsider", "Plant", "Swarm", "Undead", "Vermin",
            "Incorporeal",
        };

        public static List<string> KeysForAlignment() => new List<string> {
            "Good", "Evil", "Lawful", "Chaotic", "Neutral",
        };

        public static List<string> KeysForCondition() => new List<string> {
            "Paralyzed", "Stunned", "Frightened", "Nauseated", "Confused",
            "Blinded", "Prone", "Entangled", "Exhausted", "Fatigued",
            "Shaken", "Sickened", "Sleeping", "Petrified",
            "Slowed", "Staggered", "Dazed", "Dazzled", "Helpless",
            "Cowering", "DeathDoor",
        };

        public static List<string> LabelsForCreatureType() => Map(KeysForCreatureType(), k => $"enum.creature_type.{k}".i18n());
        public static List<string> LabelsForAlignment() => Map(KeysForAlignment(), k => $"enum.alignment.{k}".i18n());
        public static List<string> LabelsForCondition() => Map(KeysForCondition(), k => $"enum.condition.{k}".i18n());

        /// <summary>
        /// The 10 vanilla Metamagic enum values — same order as the Metamagic enum,
        /// excluding None. Used for the CastSpell Rod dropdown.
        /// </summary>
        public static readonly Metamagic[] MetamagicValues = new[] {
            Metamagic.Empower,
            Metamagic.Maximize,
            Metamagic.Quicken,
            Metamagic.Extend,
            Metamagic.Heighten,
            Metamagic.Reach,
            Metamagic.Persistent,
            Metamagic.Selective,
            Metamagic.Bolstered,
            Metamagic.CompletelyNormal,
        };

        /// <summary>
        /// Dropdown labels for the CastSpell Rod selector: "(none)" first, then the
        /// 10 metamagic types. The integer index matches RodDropdownLabels[i] →
        /// (i==0 ? null : MetamagicValues[i-1]).
        /// </summary>
        public static List<string> RodDropdownLabels() {
            var labels = new List<string>(MetamagicValues.Length + 1);
            labels.Add("cast.rod.none".i18n());
            foreach (var v in MetamagicValues) labels.Add($"enum.metamagic.{v}".i18n());
            return labels;
        }

        static List<string> Map(List<string> keys, Func<string, string> f) {
            var r = new List<string>(keys.Count);
            for (int i = 0; i < keys.Count; i++) r.Add(f(keys[i]));
            return r;
        }

        static string TypeKey(Type t) {
            switch (t.Name) {
                case "ConditionSubject":  return "subject";
                case "ConditionProperty": return "property";
                case "ActionType":        return "action";
                case "TargetType":        return "target";
                case "HealMode":          return "heal_mode";
                case "ToggleMode":        return "toggle_mode";
                case "ThrowSplashMode":   return "splash_mode";
                case "RangeBracket":      return "range";
                case "HealEnergyType":    return "heal_energy";
                default:                  return t.Name.ToLowerInvariant();
            }
        }
    }
}
