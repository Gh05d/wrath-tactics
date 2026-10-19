using System.Linq;
using WrathTactics.Engine;
using Xunit;

namespace WrathTactics.Tests {
    public class HealTargetFilterTests {
        [Fact]
        public void all_targetable_keeps_list_and_order() {
            var result = ActionValidator.KeepTargetable(new[] { "a", "b", "c" }, _ => true);
            Assert.Equal(new[] { "a", "b", "c" }, result);
        }

        [Fact]
        public void rejected_best_leaves_next_best() {
            var result = ActionValidator.KeepTargetable(new[] { "best", "next", "last" }, x => x != "best");
            Assert.Equal("next", result.First());
            Assert.Equal(new[] { "next", "last" }, result);
        }

        [Fact]
        public void all_rejected_yields_empty_list() {
            var result = ActionValidator.KeepTargetable(new[] { "a", "b" }, _ => false);
            Assert.Empty(result);
        }

        [Fact]
        public void priority_order_is_preserved_for_survivors() {
            var result = ActionValidator.KeepTargetable(new[] { 30, 10, 20, 5, 40 }, x => x != 20 && x != 5);
            Assert.Equal(new[] { 30, 10, 40 }, result);
        }
    }
}
