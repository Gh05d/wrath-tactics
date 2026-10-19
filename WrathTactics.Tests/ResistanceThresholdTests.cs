using System.Collections.Generic;
using Kingmaker.Enums.Damage;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class ResistanceThresholdTests {
        private static int Highest(DamageEnergyType energy, params (DamageEnergyType, int)[] sources)
            => ConditionEvaluator.HighestEnergyResistance(sources, energy);

        [Fact]
        public void NoSources_ReturnsZero() {
            Assert.Equal(0, Highest(DamageEnergyType.Fire));
        }

        [Fact]
        public void SingleMatchingSource_ReturnsItsValue() {
            Assert.Equal(10, Highest(DamageEnergyType.Fire, (DamageEnergyType.Fire, 10)));
        }

        [Fact]
        public void OtherEnergy_IsIgnored() {
            Assert.Equal(0, Highest(DamageEnergyType.Fire, (DamageEnergyType.Cold, 20)));
        }

        [Fact]
        public void MultipleMatchingSources_ReturnMaxNotSum() {
            Assert.Equal(10, Highest(DamageEnergyType.Fire,
                (DamageEnergyType.Fire, 5), (DamageEnergyType.Fire, 10), (DamageEnergyType.Fire, 3)));
        }

        [Fact]
        public void ZeroValueSource_ReturnsZero() {
            Assert.Equal(0, Highest(DamageEnergyType.Fire, (DamageEnergyType.Fire, 0)));
        }

        // Preset/config JSON persists numeric enum indices — appending must not shift them.
        [Fact]
        public void ConditionProperty_IndicesStayAppendOnly() {
            Assert.Equal(34, (int)ConditionProperty.WieldsRangedWeapon);
            Assert.Equal(36, (int)ConditionProperty.HasEnergyResistance);
        }
    }
}
