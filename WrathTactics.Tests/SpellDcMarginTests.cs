using System.Collections.Generic;
using WrathTactics.Engine;
using Xunit;

namespace WrathTactics.Tests {
    public class SpellDcMarginTests {
        static SaveBonusEntry E(int descriptor, bool stackable, int amount, int existing = 0) =>
            new SaveBonusEntry { Source = "t", Descriptor = descriptor, Stackable = stackable, Amount = amount, Existing = existing };

        [Fact]
        public void NetBonus_EmptyAndNull_IsZero() {
            Assert.Equal(0, SpellDcMargin.NetBonus(new List<SaveBonusEntry>()));
            Assert.Equal(0, SpellDcMargin.NetBonus(null));
        }

        [Fact]
        public void NetBonus_Stackable_AddsFully() {
            var list = new List<SaveBonusEntry> { E(1, true, 2), E(1, true, 3) };
            Assert.Equal(5, SpellDcMargin.NetBonus(list));
        }

        [Fact]
        public void NetBonus_NonStackableSameDescriptor_TakesLargest() {
            var list = new List<SaveBonusEntry> { E(7, false, 2), E(7, false, 4), E(7, false, 3) };
            Assert.Equal(4, SpellDcMargin.NetBonus(list));
        }

        [Fact]
        public void NetBonus_NonStackableDifferentDescriptors_AddUp() {
            var list = new List<SaveBonusEntry> { E(7, false, 2), E(8, false, 3) };
            Assert.Equal(5, SpellDcMargin.NetBonus(list));
        }

        [Theory]
        [InlineData(4, 2, 2)]   // exceeds existing by 2
        [InlineData(4, 4, 0)]   // equal to existing: nothing new
        [InlineData(3, 5, 0)]   // below existing: never negative
        public void NetBonus_NonStackable_OnlyExcessOverExisting(int amount, int existing, int expected) {
            Assert.Equal(expected, SpellDcMargin.NetBonus(new List<SaveBonusEntry> { E(7, false, amount, existing) }));
        }

        [Fact]
        public void NetBonus_MixedStackableAndNot() {
            var list = new List<SaveBonusEntry> { E(1, true, 2), E(7, false, 3, 1), E(7, false, 2, 1) };
            Assert.Equal(2 + 2, SpellDcMargin.NetBonus(list));
        }

        [Theory]
        [InlineData(20, 0, 11, 0, 9)]
        [InlineData(20, 0, 11, 4, 5)]    // situational bonus shrinks the margin
        [InlineData(20, 2, 11, 0, 11)]   // caster-side DC bonus grows it
        [InlineData(15, 0, 11, 6, -2)]   // negative: target likely saves
        public void Margin_Sign(int dc, int casterBonus, int baseSave, int situational, int expected) {
            Assert.Equal(expected, SpellDcMargin.Margin(dc, casterBonus, baseSave, situational));
        }
    }
}
