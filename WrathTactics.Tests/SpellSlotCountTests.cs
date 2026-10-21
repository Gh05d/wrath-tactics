using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class SpellSlotCountTests {
        static Condition Cond(ConditionOperator op, string value2) =>
            new Condition { Subject = ConditionSubject.Self, Property = ConditionProperty.SpellSlotsAtLevel,
                            Operator = op, Value = "3", Value2 = value2 };

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("abc")]
        [InlineData("-1")]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        public void Unusable_value2_means_legacy_available_greater_than_zero(string value2) {
            foreach (var op in new[] { ConditionOperator.Equal, ConditionOperator.LessThan,
                                       ConditionOperator.NotEqual, ConditionOperator.GreaterOrEqual }) {
                Assert.False(ConditionEvaluator.MatchesSlotCount(0, Cond(op, value2)));
                Assert.True(ConditionEvaluator.MatchesSlotCount(1, Cond(op, value2)));
                Assert.True(ConditionEvaluator.MatchesSlotCount(3, Cond(op, value2)));
            }
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(2, true)]
        [InlineData(3, true)]
        public void Min_count_two_with_greater_or_equal(int available, bool expected) {
            Assert.Equal(expected,
                ConditionEvaluator.MatchesSlotCount(available, Cond(ConditionOperator.GreaterOrEqual, "2")));
        }

        [Theory]
        [InlineData(0, true)]
        [InlineData(1, false)]
        public void Explicit_zero_with_equal_means_exactly_none(int available, bool expected) {
            Assert.Equal(expected,
                ConditionEvaluator.MatchesSlotCount(available, Cond(ConditionOperator.Equal, "0")));
        }

        [Theory]
        [InlineData(1, true)]
        [InlineData(2, false)]
        public void Less_than_two(int available, bool expected) {
            Assert.Equal(expected,
                ConditionEvaluator.MatchesSlotCount(available, Cond(ConditionOperator.LessThan, "2")));
        }

        [Fact]
        public void Legacy_pin_equal_with_empty_value2_ignores_operator() {
            Assert.True(ConditionEvaluator.MatchesSlotCount(3, Cond(ConditionOperator.Equal, "")));
        }

        [Theory]
        [InlineData("0", true, 0f)]
        [InlineData("2", true, 2f)]
        [InlineData(" 3 ", true, 3f)]
        [InlineData("", false, 0f)]
        [InlineData(null, false, 0f)]
        [InlineData("abc", false, 0f)]
        [InlineData("-1", false, 0f)]
        [InlineData("NaN", false, 0f)]
        [InlineData("Infinity", false, 0f)]
        [InlineData("-Infinity", false, 0f)]
        public void TryParseCountThreshold_accepts_only_finite_non_negative(string raw, bool ok, float v) {
            Assert.Equal(ok, ConditionEvaluator.TryParseCountThreshold(raw, out float parsed));
            if (ok) Assert.Equal(v, parsed);
        }
    }
}
