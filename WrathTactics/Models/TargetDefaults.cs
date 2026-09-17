namespace WrathTactics.Models {
    /// <summary>
    /// Editor-side target defaults. A fresh rule starts on TargetType.Self (enum index 0),
    /// which is meaningless for offensive actions — the player had to pick a selector by
    /// hand for every attack rule (Nexus request, 2026-09). These helpers return the target
    /// the editor should switch to when the action changes, or null to leave it alone.
    /// They only ever replace Self, so an explicit player choice is never overwritten.
    /// </summary>
    public static class TargetDefaults {
        public static TargetType? ForAction(ActionType action, TargetType current) {
            if (current != TargetType.Self) return null;
            switch (action) {
                case ActionType.AttackTarget:
                case ActionType.ThrowSplash:
                    return TargetType.EnemyHighestThreat;
                case ActionType.MoveToTarget:
                    return TargetType.EnemyNearest;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Ability pick for CastSpell/CastAbility: an ability that can only be aimed at
        /// enemies gets the threat selector; anything that can also hit self or friends is
        /// ambiguous (buff vs. debuff) and keeps the current target.
        /// </summary>
        public static TargetType? ForAbility(TargetType current, bool canTargetEnemies, bool canTargetFriends, bool canTargetSelf) {
            if (current != TargetType.Self) return null;
            if (canTargetEnemies && !canTargetFriends && !canTargetSelf)
                return TargetType.EnemyHighestThreat;
            return null;
        }
    }
}
