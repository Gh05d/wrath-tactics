using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class CompareFloatTests {
        [Theory]
        // Power-Word-style flat thresholds: the 100-HP gate
        [InlineData(100f, ConditionOperator.LessOrEqual,    100f, true)]
        [InlineData(101f, ConditionOperator.LessOrEqual,    100f, false)]
        [InlineData( 99f, ConditionOperator.LessThan,       100f, true)]
        [InlineData(100f, ConditionOperator.LessThan,       100f, false)]
        [InlineData(150f, ConditionOperator.GreaterOrEqual, 150f, true)]
        [InlineData(149f, ConditionOperator.GreaterOrEqual, 150f, false)]
        [InlineData(151f, ConditionOperator.GreaterThan,    150f, true)]
        [InlineData(150f, ConditionOperator.GreaterThan,    150f, false)]
        // Equal/NotEqual use a 0.01 epsilon
        [InlineData(100f,     ConditionOperator.Equal,    100f, true)]
        [InlineData(100.005f, ConditionOperator.Equal,    100f, true)]
        [InlineData(100.5f,   ConditionOperator.Equal,    100f, false)]
        [InlineData(100.5f,   ConditionOperator.NotEqual, 100f, true)]
        [InlineData(100f,     ConditionOperator.NotEqual, 100f, false)]
        // Clamp-at-zero boundary: dead/downed units compare as 0 on the hot path
        [InlineData(0f, ConditionOperator.LessOrEqual, 100f, true)]
        [InlineData(0f, ConditionOperator.GreaterThan,   0f, false)]
        public void CompareFloat_returns_expected(float left, ConditionOperator op,
            float right, bool expected) {
            Assert.Equal(expected, ConditionEvaluator.CompareFloat(left, op, right));
        }
    }
}
