using WrathTactics.Engine;
using Xunit;

namespace WrathTactics.Tests {
    public class CreatureTypeMatchTests {
        const string UndeadTypeGuid = "734a29b693e9ec346ba2951b27987e33";
        const string SwarmDiminutiveGuid = "2e3e840ab458ce04c92064489f87ecc2";
        const string SwarmTinyGuid = "5a04735fd0e952142bfc8ecf995e2361";
        const string IncorporealGuid = "c4a7f98d743bc784c9d4cf2105852c39";
        const string UnrelatedGuid = "00000000000000000000000000000000";

        // Nexus report 2026-07: Iz Adamantine Golems carry the item buff
        // 'WrathOfTheUndeadCountBuff' — its NAME contains "undead" but the
        // golems are constructs. Substring matching must never come back.
        [Theory]
        [InlineData("undead", "wrathoftheundeadcountbuff")]
        [InlineData("undead", "auraofcourageagainstundeadbuff")]
        [InlineData("plant", "planttypefakesomething")]
        [InlineData("animal", "animalisticbuff")]
        [InlineData("fey", "lifeytype")]
        public void NameMerelyContainingKey_DoesNotMatch(string key, string factName) {
            Assert.False(ConditionEvaluator.IsCreatureTypeFactMatch(key, factName, UnrelatedGuid));
        }

        [Theory]
        [InlineData("undead", UndeadTypeGuid)]
        [InlineData("swarm", SwarmDiminutiveGuid)]
        [InlineData("swarm", SwarmTinyGuid)]
        [InlineData("incorporeal", IncorporealGuid)]
        public void CanonicalTypeFactGuid_Matches_RegardlessOfName(string key, string guid) {
            Assert.True(ConditionEvaluator.IsCreatureTypeFactMatch(key, "whatever", guid));
        }

        [Theory]
        [InlineData("undead", "undeadtype")]
        [InlineData("humanoid", "humanoidtype")]
        [InlineData("ooze", "oozetype")]
        [InlineData("incorporeal", "incorporeal")]
        public void ExactTypeFactName_Matches_WithUnknownGuid(string key, string factName) {
            Assert.True(ConditionEvaluator.IsCreatureTypeFactMatch(key, factName, UnrelatedGuid));
        }

        [Fact]
        public void WrongKey_DoesNotMatchOtherTypesGuid() {
            Assert.False(ConditionEvaluator.IsCreatureTypeFactMatch("construct", "whatever", UndeadTypeGuid));
        }

        // Every dropdown key that has no canonical type-fact blueprint must
        // still be safe: no fact matches unless its name is the exact type name.
        [Theory]
        [InlineData("humanoid")]
        [InlineData("ooze")]
        public void KeysWithoutGuid_RejectArbitraryFacts(string key) {
            Assert.False(ConditionEvaluator.IsCreatureTypeFactMatch(key, key + "relatedbuff", UnrelatedGuid));
        }
    }
}
