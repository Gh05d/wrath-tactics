using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class RangeBracketsTests {
        [Theory]
        [InlineData(RangeBracket.Melee,   2f)]
        [InlineData(RangeBracket.Cone,    5f)]
        [InlineData(RangeBracket.Short,   10f)]
        [InlineData(RangeBracket.Medium,  20f)]
        [InlineData(RangeBracket.Long,    40f)]
        public void MaxMeters_returns_expected(RangeBracket bracket, float expected) {
            Assert.Equal(expected, RangeBrackets.MaxMeters(bracket));
        }

        [Fact]
        public void MaxMeters_returns_PositiveInfinity_for_unknown_bracket() {
            Assert.Equal(float.PositiveInfinity, RangeBrackets.MaxMeters((RangeBracket)999));
        }
    }
}
