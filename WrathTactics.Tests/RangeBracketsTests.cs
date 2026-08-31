using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class RangeBracketsTests {
        [Theory]
        [InlineData(RangeBracket.Melee,   2f)]
        [InlineData(RangeBracket.Cone,    5f)]
        [InlineData(RangeBracket.Short,   10f)]
        [InlineData(RangeBracket.Medium,  20f)]
        [InlineData(RangeBracket.Far,     30f)]
        [InlineData(RangeBracket.Long,    40f)]
        public void MaxMeters_returns_expected(RangeBracket bracket, float expected) {
            Assert.Equal(expected, RangeBrackets.MaxMeters(bracket));
        }

        [Fact]
        public void MaxMeters_returns_PositiveInfinity_for_unknown_bracket() {
            Assert.Equal(float.PositiveInfinity, RangeBrackets.MaxMeters((RangeBracket)999));
        }

        [Theory]
        // Short: lo=5, hi=10 — the report's trap: "<" means BELOW the bracket.
        [InlineData(RangeBracket.Short, ConditionOperator.LessThan,       "≤ 5 m")]
        [InlineData(RangeBracket.Short, ConditionOperator.LessOrEqual,    "≤ 10 m")]
        [InlineData(RangeBracket.Short, ConditionOperator.GreaterOrEqual, "> 5 m")]
        [InlineData(RangeBracket.Short, ConditionOperator.GreaterThan,    "> 10 m")]
        [InlineData(RangeBracket.Short, ConditionOperator.Equal,          "5–10 m")]
        [InlineData(RangeBracket.Short, ConditionOperator.NotEqual,       "≠ 5–10 m")]
        // Melee: lo=0 — "<" yields the visibly-never-true "≤ 0 m".
        [InlineData(RangeBracket.Melee, ConditionOperator.LessThan,       "≤ 0 m")]
        [InlineData(RangeBracket.Melee, ConditionOperator.Equal,          "0–2 m")]
        // Far: lo=20, hi=30 — the "<= 30 m" band requested on Nexus.
        [InlineData(RangeBracket.Far,   ConditionOperator.LessOrEqual,    "≤ 30 m")]
        [InlineData(RangeBracket.Far,   ConditionOperator.LessThan,       "≤ 20 m")]
        [InlineData(RangeBracket.Far,   ConditionOperator.GreaterThan,    "> 30 m")]
        [InlineData(RangeBracket.Far,   ConditionOperator.Equal,          "20–30 m")]
        // Long keeps its pre-Far interval: existing saved Long rules must
        // not silently narrow to 30–40 m (Far overlaps Long by design).
        [InlineData(RangeBracket.Long,  ConditionOperator.Equal,          "20–40 m")]
        [InlineData(RangeBracket.Long,  ConditionOperator.LessThan,       "≤ 20 m")]
        public void EffectiveHint_maps_operator_to_evaluator_interval(
            RangeBracket b, ConditionOperator op, string expected) {
            Assert.Equal(expected, RangeBrackets.EffectiveHint(b, op));
        }

        [Fact]
        public void EffectiveHint_unknown_operator_returns_null() {
            Assert.Null(RangeBrackets.EffectiveHint(RangeBracket.Short, (ConditionOperator)99));
        }
    }
}
