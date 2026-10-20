using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class ResourceAmountTests {
        static Condition Cond(ConditionOperator op, string value2) =>
            new Condition { Subject = ConditionSubject.Self, Property = ConditionProperty.Resource,
                            Operator = op, Value = "guid", Value2 = value2 };

        [Fact]
        public void Zero_amount_with_equal_matches_empty_pool() =>
            Assert.True(ConditionEvaluator.MatchesResourceAmount(0, Cond(ConditionOperator.Equal, "0")));

        [Fact]
        public void Zero_amount_with_equal_rejects_filled_pool() =>
            Assert.False(ConditionEvaluator.MatchesResourceAmount(1, Cond(ConditionOperator.Equal, "0")));

        [Theory]
        [InlineData(1, false)]
        [InlineData(2, true)]
        [InlineData(3, true)]
        public void Min_two_with_greater_or_equal(int remaining, bool expected) =>
            Assert.Equal(expected,
                ConditionEvaluator.MatchesResourceAmount(remaining, Cond(ConditionOperator.GreaterOrEqual, "2")));

        [Fact]
        public void Empty_value2_with_equal_is_legacy_remaining_above_zero_when_filled() =>
            Assert.True(ConditionEvaluator.MatchesResourceAmount(3, Cond(ConditionOperator.Equal, "")));

        [Fact]
        public void Empty_value2_with_equal_is_legacy_remaining_above_zero_when_empty() =>
            Assert.False(ConditionEvaluator.MatchesResourceAmount(0, Cond(ConditionOperator.Equal, "")));

        [Theory]
        [InlineData("abc")]
        [InlineData(null)]
        [InlineData("-1")]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        public void Unusable_value2_falls_back_to_legacy_remaining_above_zero(string value2) {
            Assert.True(ConditionEvaluator.MatchesResourceAmount(1, Cond(ConditionOperator.LessThan, value2)));
            Assert.False(ConditionEvaluator.MatchesResourceAmount(0, Cond(ConditionOperator.LessThan, value2)));
        }
    }
}
