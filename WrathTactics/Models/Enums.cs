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
        EnemyHighestWill     // the single enemy with highest Will save
    }

    public enum ConditionProperty {
        HpPercent,
        AC,
        HasBuff,
        MissingBuff,
        HasCondition,
        HasDebuff,
        SpellSlotsAtLevel,
        SpellSlotsAboveLevel,
        Resource,
        CreatureType,
        CombatRounds,
        IsDead,
        SaveFortitude,
        SaveReflex,
        SaveWill,
        Alignment
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
        ConditionTarget    // the enemy/ally that matched the triggering condition
    }
}
