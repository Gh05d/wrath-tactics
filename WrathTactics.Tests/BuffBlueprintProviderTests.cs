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

        [Fact]
        public void FormatDisplayLabel_appends_internal_name_when_distinct() {
            // Witch vs. Shaman "Evil Eye" share a localized name but are distinct
            // blueprints — the internal id must be surfaced to disambiguate.
            var result = BuffBlueprintProvider.FormatDisplayLabel("Evil Eye", "WitchEvilEyeHexBuff");
            Assert.StartsWith("Evil Eye", result);
            Assert.Contains("WitchEvilEyeHexBuff", result);
            Assert.Contains("(", result);
        }

        [Theory]
        [InlineData("BlessBuff", "BlessBuff")]   // identical (hidden buff w/o display name) -> no suffix
        [InlineData("Bless",     "")]            // no internal name -> bare display name
        [InlineData("Bless",     null)]
        public void FormatDisplayLabel_returns_bare_name_when_no_distinct_internal(string name, string intName) {
            Assert.Equal(name, BuffBlueprintProvider.FormatDisplayLabel(name, intName));
        }
    }
}
