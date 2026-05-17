namespace WrathTactics.Engine {
    /// <summary>
    /// Source list of a tactics rule, used by ActiveRuleTracker to derive priority
    /// gates. Globals are unconditionally higher priority than Characters — the
    /// evaluator iterates GlobalRules before character rules each tick.
    /// </summary>
    public enum RuleListSource {
        Global,
        Character,
    }
}
