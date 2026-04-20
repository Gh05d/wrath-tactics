namespace WrathTactics.Models {
    public enum ConditionSubject {
        Self,
        Ally,
        AllyCount,
        Enemy,
        EnemyCount,
        Combat,
        EnemyBiggestThreat,  // the single enemy with highest threat
        EnemyLowestThreat,   // the single enemy with lowest threat
        EnemyHighestHp,      // the single enemy with highest HP%
        EnemyLowestHp,       // the single enemy with lowest HP%
        EnemyLowestAC,       // the single enemy with lowest AC
        EnemyHighestAC,      // the single enemy with highest AC
        EnemyLowestFort,     // the single enemy with lowest Fortitude save
        EnemyHighestFort,    // the single enemy with highest Fortitude save
        EnemyLowestReflex,   // the single enemy with lowest Reflex save
        EnemyHighestReflex,  // the single enemy with highest Reflex save
        EnemyLowestWill,     // the single enemy with lowest Will save
        EnemyHighestWill,    // the single enemy with highest Will save
        EnemyHighestHD,      // the single enemy with highest HD
        EnemyLowestHD        // the single enemy with lowest HD
    }

    public enum ConditionProperty {
        HpPercent,
        AC,
        HasBuff,
        HasCondition,
        SpellSlotsAtLevel,
        SpellSlotsAboveLevel,
        Resource,
        CreatureType,
        CombatRounds,
        IsDead,
        SaveFortitude,
        SaveReflex,
        SaveWill,
        Alignment,
        IsInCombat,
        HitDice,
        SpellDCMinusSave,
        HasClass,
        WithinRange
    }

    public enum ConditionOperator {
        LessThan,
        GreaterThan,
        Equal,
        NotEqual,
        GreaterOrEqual,
        LessOrEqual
    }

    public enum ActionType {
        CastSpell,
        CastAbility,    // class abilities (non-spell, non-item)
        UseItem,
        ToggleActivatable,
        AttackTarget,
        Heal,           // automatically use best available heal
        DoNothing,
        ThrowSplash     // throw a splash weapon (Alchemist's Fire, Acid Flask, Holy Water)
    }

    public enum HealMode {
        Any,            // Use any available heal (spell > scroll > potion)
        Strongest,      // Use the highest-level heal available
        Weakest         // Use the lowest-level heal (conserve resources)
    }

    /// <summary>
    /// Which classes of heal source the engine may draw from. Flag-based so combinations
    /// (e.g. Spell+Potion to skip scrolls when UMD is bad) are expressible. Default is All.
    /// Spell covers spellbook casts, class abilities (Channel, Lay on Hands), and wands/staves.
    /// </summary>
    [System.Flags]
    public enum HealSourceMask {
        None   = 0,
        Spell  = 1,
        Scroll = 2,
        Potion = 4,
        All    = Spell | Scroll | Potion,
    }

    public enum ThrowSplashMode {
        Any,        // Use whatever splash item is first in inventory
        Strongest,  // Use the highest-damage splash item
        Cheapest    // Use the lowest-cost splash item
    }

    public enum ToggleMode {
        On,
        Off
    }

    public enum TargetType {
        Self,
        AllyLowestHp,
        AllyWithCondition,
        AllyMissingBuff,
        EnemyNearest,
        EnemyLowestHp,
        EnemyHighestHp,
        EnemyHighestAC,
        EnemyLowestAC,
        EnemyHighestFort,
        EnemyLowestFort,
        EnemyHighestReflex,
        EnemyLowestReflex,
        EnemyHighestWill,
        EnemyLowestWill,
        EnemyHighestThreat,
        EnemyCreatureType,
        ConditionTarget,    // the enemy/ally that matched the triggering condition
        EnemyHighestHD,
        EnemyLowestHD,
        PointAtSelf,            // ~1 square in front of caster
        PointAtConditionTarget  // ~1 square toward caster from matched unit
    }

    public enum RangeBracket { Melee, Cone, Short, Medium, Long }

    public static class RangeBrackets {
        public static float MaxMeters(RangeBracket b) {
            switch (b) {
                case RangeBracket.Melee:  return 2f;
                case RangeBracket.Cone:   return 5f;
                case RangeBracket.Short:  return 10f;
                case RangeBracket.Medium: return 20f;
                case RangeBracket.Long:   return 40f;
                default:                  return float.PositiveInfinity;
            }
        }

        public static bool TryParse(string s, out RangeBracket b) {
            return System.Enum.TryParse(s, ignoreCase: true, result: out b);
        }

        public static float LowerMeters(RangeBracket b) {
            switch (b) {
                case RangeBracket.Melee:  return 0f;
                case RangeBracket.Cone:   return MaxMeters(RangeBracket.Melee);
                case RangeBracket.Short:  return MaxMeters(RangeBracket.Cone);
                case RangeBracket.Medium: return MaxMeters(RangeBracket.Short);
                case RangeBracket.Long:   return MaxMeters(RangeBracket.Medium);
                default:                  return 0f;
            }
        }

        public static string Label(RangeBracket b) {
            switch (b) {
                case RangeBracket.Melee:  return "Melee (≤2 m)";
                case RangeBracket.Cone:   return "Cone (≤5 m)";
                case RangeBracket.Short:  return "Short (≤10 m)";
                case RangeBracket.Medium: return "Medium (≤20 m)";
                case RangeBracket.Long:   return "Long (≤40 m)";
                default:                  return b.ToString();
            }
        }
    }
}
