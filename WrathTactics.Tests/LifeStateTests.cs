using Kingmaker.UnitLogic;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    // IsDowned = felled in combat (LifeState==Dead) but not finally dead. Unconscious
    // (Death's Door) is deliberately not downed. ConditionProperty persists as an enum index:
    // IsDead stays 9, new entries append at the end.
    public class LifeStateTests {
        [Theory]
        [InlineData(UnitLifeState.Conscious, false, false)]
        [InlineData(UnitLifeState.Unconscious, false, false)]
        [InlineData(UnitLifeState.Dead, false, true)]
        [InlineData(UnitLifeState.Dead, true, false)]
        [InlineData(UnitLifeState.Conscious, true, false)]
        public void IsDownedState_is_true_only_for_dead_but_not_finally_dead(
            UnitLifeState lifeState, bool isFinallyDead, bool expected) {
            Assert.Equal(expected, ConditionEvaluator.IsDownedState(lifeState, isFinallyDead));
        }

        [Fact]
        public void ConditionProperty_indices_stay_stable() {
            Assert.Equal(9, (int)ConditionProperty.IsDead);
            Assert.Equal(34, (int)ConditionProperty.WieldsRangedWeapon);
            Assert.Equal(38, (int)ConditionProperty.IsDowned);
        }
    }
}
