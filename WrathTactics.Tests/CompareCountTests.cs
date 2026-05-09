using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class CompareCountTests {
        [Theory]
        [InlineData(3, 3f, ConditionOperator.Equal,           true)]
        [InlineData(2, 3f, ConditionOperator.Equal,           false)]
        [InlineData(2, 3f, ConditionOperator.LessThan,        true)]
        [InlineData(3, 3f, ConditionOperator.LessThan,        false)]
        [InlineData(3, 3f, ConditionOperator.LessOrEqual,     true)]
        [InlineData(4, 3f, ConditionOperator.LessOrEqual,     false)]
        [InlineData(4, 3f, ConditionOperator.GreaterThan,     true)]
        [InlineData(3, 3f, ConditionOperator.GreaterThan,     false)]
        [InlineData(3, 3f, ConditionOperator.GreaterOrEqual,  true)]
        [InlineData(2, 3f, ConditionOperator.GreaterOrEqual,  false)]
        [InlineData(2, 3f, ConditionOperator.NotEqual,        true)]
        [InlineData(3, 3f, ConditionOperator.NotEqual,        false)]
        public void CompareCount_returns_expected(int actual, float threshold,
            ConditionOperator op, bool expected) {
            Assert.Equal(expected, ConditionEvaluator.CompareCount(actual, threshold, op));
        }
    }
}
