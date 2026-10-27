using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    // IsBeingCastOnByAlly: the ability filter is a pure comparison so it can be tested
    // without game objects. Exact-only GUID matching (never Contains), case-insensitive.
    public class TargetingRelationsTests {
        const string Heal = "aaaa0000000000000000000000000001";
        const string Other = "bbbb0000000000000000000000000002";
        const string Parent = "cccc0000000000000000000000000003";

        [Fact]
        public void IsBeingCastOnByAlly_enum_index_is_pinned() {
            Assert.Equal(36, (int)ConditionProperty.IsBeingCastOnByAlly);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Empty_filter_matches_any_cast_even_without_guids(string filter) {
            Assert.True(TargetingRelations.MatchesAbilityFilter(filter, Heal, Parent));
            Assert.True(TargetingRelations.MatchesAbilityFilter(filter, null, null));
        }

        [Fact]
        public void Same_guid_matches() {
            Assert.True(TargetingRelations.MatchesAbilityFilter(Heal, Heal, null));
        }

        [Fact]
        public void Same_guid_matches_case_insensitively() {
            Assert.True(TargetingRelations.MatchesAbilityFilter(Heal.ToUpperInvariant(), Heal, null));
        }

        [Fact]
        public void Other_guid_does_not_match() {
            Assert.False(TargetingRelations.MatchesAbilityFilter(Other, Heal, null));
        }

        [Fact]
        public void Substring_of_guid_does_not_match() {
            Assert.False(TargetingRelations.MatchesAbilityFilter(Heal.Substring(0, 8), Heal, null));
        }

        [Fact]
        public void Converted_from_guid_matches_variant_cast() {
            Assert.True(TargetingRelations.MatchesAbilityFilter(Parent, Heal, Parent));
        }

        [Fact]
        public void Non_empty_filter_does_not_match_cast_without_guids() {
            Assert.False(TargetingRelations.MatchesAbilityFilter(Heal, null, null));
        }

        [Fact]
        public void HasCast_without_units_is_false() {
            Assert.False(TargetingRelations.HasCast(null, null, ""));
        }
    }
}
