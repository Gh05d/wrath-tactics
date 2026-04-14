namespace WrathTactics.Models {
    public enum ConditionSubject {
        Self,
        Ally,
        AllyCount,
        Enemy,
        EnemyCount,
        Combat,
        EnemyBiggestThreat,  // the single enemy with highest threat
        EnemyLowestThreat    // the single enemy with lowest threat
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
        IsDead
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
        DoNothing
    }

    public enum HealMode {
        Any,            // Use any available heal (spell > scroll > potion)
        Strongest,      // Use the highest-level heal available
        Weakest         // Use the lowest-level heal (conserve resources)
    }

    public enum TargetType {
        Self,
        AllyLowestHp,
        AllyWithCondition,
        AllyMissingBuff,
        EnemyNearest,
        EnemyLowestHp,
        EnemyHighestAC,
        EnemyHighestThreat,
        EnemyCreatureType,
        ConditionTarget    // the enemy/ally that matched the triggering condition
    }
}
