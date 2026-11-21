using System.Collections.Generic;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class SummonPoolTests {
        static Condition C(ConditionSubject s, ConditionProperty p) =>
            new Condition { Subject = s, Property = p };

        [Fact]
        public void IsSummonRowWidensPool() {
            var conds = new List<Condition> { C(ConditionSubject.AllyCount, ConditionProperty.IsSummon) };
            Assert.True(ConditionEvaluator.BucketAsksForSummons(conds));
        }

        [Fact]
        public void OtherRowsKeepPartyOnlyPool() {
            var conds = new List<Condition> {
                C(ConditionSubject.AllyCount, ConditionProperty.HpPercent),
                C(ConditionSubject.Ally, ConditionProperty.IsDead),
            };
            Assert.False(ConditionEvaluator.BucketAsksForSummons(conds));
        }
    }
}
