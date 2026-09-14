namespace WrathTactics.Engine {
    /// <summary>What TacticsEvaluator should do with a rule cooldown once the command the
    /// rule issued has been observed again.</summary>
    internal enum IssuedCommandOutcome {
        /// <summary>Still pending, queued or running — look again next tick.</summary>
        Keep,
        /// <summary>The command acted; the cooldown stands. Stop tracking.</summary>
        Spent,
        /// <summary>The command never acted — interrupted before its act point, or wiped
        /// from slot and queue before it started. The unit spent no action, so the rule
        /// cooldown is refunded and the rule may retry next tick.</summary>
        Refund,
    }

    internal static class IssuedCommandPolicy {
        /// <summary>
        /// Pure classifier. The engine charges the action at the act point
        /// (UnitCommandController.TickCommand → SpendAction after IsActed), so anything that
        /// ends without acting cost the unit nothing: a cast cancelled by a player move
        /// order, a cast pre-empted by a higher rule, a queued command wiped by a later
        /// Run(). A full attack interrupted after its first swing has acted and stays spent.
        /// </summary>
        internal static IssuedCommandOutcome Classify(bool started, bool finished, bool acted, bool resident) {
            if (finished) return acted ? IssuedCommandOutcome.Spent : IssuedCommandOutcome.Refund;
            if (!started && !resident) return IssuedCommandOutcome.Refund;
            return IssuedCommandOutcome.Keep;
        }
    }
}
