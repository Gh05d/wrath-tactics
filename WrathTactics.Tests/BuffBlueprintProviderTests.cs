using System;
using WrathTactics.Engine;
using Xunit;

namespace WrathTactics.Tests {
    public class BuffBlueprintProviderTests {
        [Theory]
        [InlineData("ArmyBuff",                 true)]
        [InlineData("ArmyHaste",                true)]
        [InlineData("armyBuff",                 true)]    // case-insensitive
        [InlineData("BlessBuff",                false)]
        [InlineData("AirBlessingMajorBuff",     false)]   // real Warpriest blessing
        [InlineData("",                          false)]
        public void IsCrusadeOnlyBuff_classifies_by_Army_prefix(string name, bool expected) {
            Assert.Equal(expected, BuffBlueprintProvider.IsCrusadeOnlyBuff(name));
        }

        [Fact]
        public void IsCrusadeOnlyBuff_throws_on_null() {
            Assert.Throws<NullReferenceException>(
                () => BuffBlueprintProvider.IsCrusadeOnlyBuff(null));
        }
    }
}
