using System.Linq;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class EngagedEnemyCountTests {
        [Fact]
        public void Enum_index_is_pinned_because_presets_persist_numeric_values() {
            Assert.Equal(35, (int)ConditionProperty.EngagedEnemyCount);
            Assert.True(ConditionProperty.EngagedEnemyCount > ConditionProperty.WieldsRangedWeapon);
            Assert.Equal(28, (int)ConditionProperty.AdjacentEnemyCount);
        }

        static int Count(string[] engaged, string[] engagedBy, System.Func<string, bool> keep = null) =>
            ConditionEvaluator.CountDistinct(engaged, engagedBy, keep ?? (_ => true));

        [Fact]
        public void Both_sides_null_count_zero() {
            Assert.Equal(0, Count(null, null));
        }

        [Fact]
        public void Only_engaged_side_is_counted() {
            Assert.Equal(2, Count(new[] { "a", "b" }, null));
        }

        [Fact]
        public void Only_engagedBy_side_is_counted() {
            Assert.Equal(1, Count(null, new[] { "c" }));
        }

        [Fact]
        public void Duplicate_within_one_side_is_counted_once() {
            Assert.Equal(1, Count(new[] { "a", "a" }, null));
            Assert.Equal(1, Count(null, new[] { "b", "b" }));
        }

        [Fact]
        public void Overlap_is_counted_once() {
            Assert.Equal(3, Count(new[] { "a", "b" }, new[] { "b", "c" }));
        }

        [Fact]
        public void Keep_filter_drops_rejected_entries_from_both_sides() {
            Assert.Equal(1, Count(new[] { "a", "x" }, new[] { "x", "b" }, e => e == "b"));
        }
    }
}
