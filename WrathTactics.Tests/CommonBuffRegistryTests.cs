using System;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class CommonBuffRegistryTests {
        [Theory]
        [InlineData(ConditionSubject.Enemy,              true)]
        [InlineData(ConditionSubject.EnemyCount,         true)]
        [InlineData(ConditionSubject.EnemyBiggestThreat, true)]
        [InlineData(ConditionSubject.EnemyHighestHp,     true)]
        [InlineData(ConditionSubject.EnemyLowestAC,      true)]
        [InlineData(ConditionSubject.EnemyHighestWill,   true)]
        [InlineData(ConditionSubject.Self,               false)]
        [InlineData(ConditionSubject.Ally,               false)]
        [InlineData(ConditionSubject.AllyCount,          false)]
        [InlineData(ConditionSubject.AllyByName,         false)]
        [InlineData(ConditionSubject.Combat,             false)]
        [InlineData(ConditionSubject.EnemyHighestHD,     true)]
        [InlineData(ConditionSubject.EnemyLowestHD,      true)]
        public void IsEnemySubject_classifies_correctly(ConditionSubject subject, bool expected) {
            Assert.Equal(expected, CommonBuffRegistry.IsEnemySubject(subject));
        }

        [Fact]
        public void IsEnemySubject_handles_every_enum_value_without_throwing() {
            foreach (ConditionSubject value in Enum.GetValues(typeof(ConditionSubject))) {
                var _ = CommonBuffRegistry.IsEnemySubject(value);
            }
        }

        [Fact]
        public void GetDefaultGuids_returns_nonnull_for_Self() {
            // Note: in pure-logic test runtime BuffBlueprintProvider.Load() falls into
            // its catch-block (no initialised Game.Instance), so the returned list is
            // empty. We assert nonnull rather than nonempty.
            var guids = CommonBuffRegistry.GetDefaultGuids(ConditionSubject.Self);
            Assert.NotNull(guids);
        }

        [Fact]
        public void GetDefaultGuids_returns_nonnull_for_Enemy() {
            var guids = CommonBuffRegistry.GetDefaultGuids(ConditionSubject.Enemy);
            Assert.NotNull(guids);
        }

        [Fact]
        public void GetDefaultGuids_uses_separate_caches_for_ally_vs_enemy() {
            // Identity-checks: both ally-classified subjects return the SAME cached list,
            // both enemy-classified subjects return the SAME cached list (different from ally's).
            var allySelf  = CommonBuffRegistry.GetDefaultGuids(ConditionSubject.Self);
            var allyOther = CommonBuffRegistry.GetDefaultGuids(ConditionSubject.Ally);
            var enemy     = CommonBuffRegistry.GetDefaultGuids(ConditionSubject.Enemy);
            var enemyHp   = CommonBuffRegistry.GetDefaultGuids(ConditionSubject.EnemyHighestHp);

            Assert.Same(allySelf, allyOther);
            Assert.Same(enemy, enemyHp);
            Assert.NotSame(allySelf, enemy);
        }
    }
}
