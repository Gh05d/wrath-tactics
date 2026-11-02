using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    // Classification helpers shared by TargetResolver and
    // TacticsEvaluator (ally-only condition targets must skip instead of falling back to the owner).
    public class TargetTypeTraitsTests {
        [Theory]
        [InlineData(TargetType.ConditionTargetAlly)]
        public void IsAllyConditionOnly_true_for_ally_condition_target(TargetType type) {
            Assert.True(TargetTypeTraits.IsAllyConditionOnly(type));
        }

        [Theory]
        [InlineData(TargetType.ConditionTarget)]
        [InlineData(TargetType.PointAtConditionTarget)]
        [InlineData(TargetType.SpecificAlly)]
        [InlineData(TargetType.Self)]
        public void IsAllyConditionOnly_false_for_other_targets(TargetType type) {
            Assert.False(TargetTypeTraits.IsAllyConditionOnly(type));
        }

        [Theory]
        [InlineData(TargetType.ConditionTargetAlly)]
        public void SkipsWhenUnresolved_true_for_empty_ally_condition_target(TargetType type) {
            Assert.True(TargetTypeTraits.SkipsWhenUnresolved(type, targetValid: false));
        }

        [Theory]
        [InlineData(TargetType.ConditionTargetAlly)]
        public void SkipsWhenUnresolved_false_when_ally_resolved(TargetType type) {
            Assert.False(TargetTypeTraits.SkipsWhenUnresolved(type, targetValid: true));
        }

        [Theory]
        [InlineData(TargetType.Self)]
        [InlineData(TargetType.ConditionTarget)]
        [InlineData(TargetType.PointAtConditionTarget)]
        [InlineData(TargetType.AllyLowestHp)]
        public void SkipsWhenUnresolved_false_for_other_targets(TargetType type) {
            // Heal/Toggle on these keep the CommandExecutor owner fallback.
            Assert.False(TargetTypeTraits.SkipsWhenUnresolved(type, targetValid: false));
        }

        [Fact]
        public void AllyConditionTarget_returns_latched_ally() {
            var owner = new object();
            var ally = new object();
            Assert.Same(ally, TargetTypeTraits.AllyConditionTarget(ally, owner));
        }

        [Fact]
        public void AllyConditionTarget_null_when_owner_latched() {
            var owner = new object();
            Assert.Null(TargetTypeTraits.AllyConditionTarget(owner, owner));
        }

        [Fact]
        public void AllyConditionTarget_null_when_nothing_latched() {
            Assert.Null(TargetTypeTraits.AllyConditionTarget<object>(null, new object()));
        }
    }
}
