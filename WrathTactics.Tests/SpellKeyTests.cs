using WrathTactics.UI;
using Xunit;

namespace WrathTactics.Tests {
    // Compound ability keys: guid[@L<level>][>V<variantGuid>][~A<actionType>][#<metamagicMask>]
    // The ~A segment discriminates conversions that share the parent's blueprint GUID
    // (e.g. TTT Quick Channel's move-action channel) and must survive ParseKey roundtrips
    // without disturbing legacy keys, which never carry it.
    public class SpellKeyTests {
        const string Guid = "e2d75b899edb49dd8b3d9912009b63a4";
        const string Parent = "b9eca127dd82f554fb2ccd804de86cf6";

        [Fact]
        public void ParseKey_bare_guid_defaults() {
            var p = SpellDropdownProvider.ParseKey(Guid);
            Assert.Equal(Guid, p.BlueprintGuid);
            Assert.Equal(-1, p.Level);
            Assert.Null(p.VariantGuid);
            Assert.Equal(0, p.MetamagicMask);
            Assert.Equal(-1, p.ActionType);
        }

        [Fact]
        public void ParseKey_level_and_variant() {
            var p = SpellDropdownProvider.ParseKey($"{Parent}@L3>V{Guid}");
            Assert.Equal(Parent, p.BlueprintGuid);
            Assert.Equal(3, p.Level);
            Assert.Equal(Guid, p.VariantGuid);
            Assert.Equal(-1, p.ActionType);
        }

        [Fact]
        public void ParseKey_full_key_with_actiontype_and_mask() {
            var p = SpellDropdownProvider.ParseKey($"{Parent}@L3>V{Guid}~A2#12");
            Assert.Equal(Parent, p.BlueprintGuid);
            Assert.Equal(3, p.Level);
            Assert.Equal(Guid, p.VariantGuid);
            Assert.Equal(2, p.ActionType);
            Assert.Equal(12, p.MetamagicMask);
        }

        [Fact]
        public void ParseKey_same_guid_conversion() {
            var p = SpellDropdownProvider.ParseKey($"{Parent}>V{Parent}~A1");
            Assert.Equal(Parent, p.BlueprintGuid);
            Assert.Equal(Parent, p.VariantGuid);
            Assert.Equal(1, p.ActionType);
            Assert.Equal(-1, p.Level);
            Assert.Equal(0, p.MetamagicMask);
        }

        [Fact]
        public void ParseKey_legacy_mask_key_unaffected() {
            var p = SpellDropdownProvider.ParseKey($"{Guid}#4");
            Assert.Equal(Guid, p.BlueprintGuid);
            Assert.Equal(4, p.MetamagicMask);
            Assert.Equal(-1, p.ActionType);
        }

        [Theory]
        [InlineData(-1, false)]
        [InlineData(0, true)]
        [InlineData(2, true)]
        public void MakeKeyCore_emits_actiontype_segment_only_when_set(int actionType, bool expectSegment) {
            var key = SpellDropdownProvider.MakeKeyCore(Parent, 3, Guid, 0, actionType);
            Assert.Equal(expectSegment, key.Contains("~A"));
        }

        [Fact]
        public void MakeKeyCore_roundtrips_through_ParseKey() {
            var key = SpellDropdownProvider.MakeKeyCore(Parent, 3, Guid, 12, 2);
            var p = SpellDropdownProvider.ParseKey(key);
            Assert.Equal(Parent, p.BlueprintGuid);
            Assert.Equal(3, p.Level);
            Assert.Equal(Guid, p.VariantGuid);
            Assert.Equal(2, p.ActionType);
            Assert.Equal(12, p.MetamagicMask);
        }

        [Fact]
        public void MakeKeyCore_bare_guid_when_nothing_set() {
            Assert.Equal(Guid, SpellDropdownProvider.MakeKeyCore(Guid, -1, null, 0, -1));
        }
    }
}
