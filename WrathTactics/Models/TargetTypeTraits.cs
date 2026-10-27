namespace WrathTactics.Models {
    /// <summary>
    /// Pure classification of TargetType members, shared by TargetResolver and
    /// TacticsEvaluator (ally-only condition targets must skip the rule when empty).
    /// </summary>
    public static class TargetTypeTraits {
        public static bool IsAllyConditionOnly(TargetType type) {
            return type == TargetType.ConditionTargetAlly;
        }

        // An empty ally-only condition target skips the rule; every other type keeps
        // its existing path (RequiresValidTarget / CommandExecutor owner fallback).
        public static bool SkipsWhenUnresolved(TargetType type, bool targetValid) {
            return !targetValid && IsAllyConditionOnly(type);
        }

        // The latched ally, unless it is the owner (an AllyCount-only group or an
        // AllyByName row naming the owner can latch it). Generic so tests need no game unit.
        public static T AllyConditionTarget<T>(T latchedAlly, T owner) where T : class {
            return ReferenceEquals(latchedAlly, owner) ? null : latchedAlly;
        }
    }
}
