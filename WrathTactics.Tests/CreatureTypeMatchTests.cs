using WrathTactics.Engine;
using Xunit;

namespace WrathTactics.Tests {
    public class CreatureTypeMatchTests {
        const string UndeadTypeGuid = "734a29b693e9ec346ba2951b27987e33";
        const string SwarmDiminutiveGuid = "2e3e840ab458ce04c92064489f87ecc2";
        const string SwarmTinyGuid = "5a04735fd0e952142bfc8ecf995e2361";
        const string IncorporealGuid = "c4a7f98d743bc784c9d4cf2105852c39";
        const string SubtypeExtraplanarGuid = "136fa0343d5b4b348bdaa05d83408db3";
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

        // Humanoid is absence-defined (see IsNonHumanoidFact): these facts are what
        // HoldPerson itself excludes, so each one must disqualify a unit.
        [Theory]
        [InlineData("whatever", UndeadTypeGuid)]
        [InlineData("whatever", SwarmDiminutiveGuid)]
        [InlineData("whatever", SwarmTinyGuid)]
        [InlineData("whatever", SubtypeExtraplanarGuid)]
        [InlineData("undeadtype", UnrelatedGuid)]
        [InlineData("dragontype", UnrelatedGuid)]
        [InlineData("monstroushumanoidtype", UnrelatedGuid)]
        [InlineData("subtypeextraplanar", UnrelatedGuid)]
        public void NonHumanoidFact_DisqualifiesHumanoid(string factName, string factGuid) {
            Assert.True(ConditionEvaluator.IsNonHumanoidFact(factName, factGuid));
        }

        // The substring trap in reverse: an incidentally named buff must not strip
        // humanoid off a real humanoid. Incorporeal is a subtype, not a creature type —
        // HoldPerson does not exclude it, so neither do we.
        [Theory]
        [InlineData("wrathoftheundeadcountbuff", UnrelatedGuid)]
        [InlineData("animalisticbuff", UnrelatedGuid)]
        [InlineData("planttypefakesomething", UnrelatedGuid)]
        [InlineData("extraplanarbindingbuff", UnrelatedGuid)]
        [InlineData("whatever", IncorporealGuid)]
        [InlineData("oozetype", UnrelatedGuid)]
        public void UnrelatedOrNonExcludedFact_KeepsHumanoid(string factName, string factGuid) {
            Assert.False(ConditionEvaluator.IsNonHumanoidFact(factName, factGuid));
        }
    }
}
