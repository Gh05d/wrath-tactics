namespace WrathTactics.Models {
    public enum ConditionSubject {
        Self,
        Ally,
        AllyCount,
        Enemy,
        EnemyCount,
        Combat
    }

    public enum ConditionProperty {
        HpPercent,
        AC,
        HasBuff,
        MissingBuff,
        HasCondition,
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
        UseItem,
        ToggleActivatable,
        AttackTarget,
        DoNothing
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
        EnemyCreatureType
    }
}
