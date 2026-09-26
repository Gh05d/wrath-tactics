using UnityEngine;
using WrathTactics.UI;
using Xunit;

namespace WrathTactics.Tests {
    /// <summary>
    /// Image.color multiplies the band sprite. PackBandTint must therefore never darken the
    /// band (max channel stays 1) while keeping the palette hue (channel ratios preserved) —
    /// otherwise rust/plum pack headers turn black and unreadable (spec Review Focus 5).
    /// </summary>
    public class ThemeTests {
        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
        public void PackBandTint_KeepsMaxChannelAtOne(int index) {
            var tint = Theme.PackBandTint(index);
            float max = Mathf.Max(tint.r, Mathf.Max(tint.g, tint.b));
            Assert.Equal(1f, max, 5);
            Assert.Equal(1f, tint.a, 5);
        }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
        public void PackBandTint_PreservesHueRatios(int index) {
            var src = PackPalette.ColorAt(index);
            var tint = Theme.PackBandTint(index);
            // r:g and r:b ratios survive the normalisation.
            Assert.Equal(src.r / src.g, tint.r / tint.g, 3);
            Assert.Equal(src.r / src.b, tint.r / tint.b, 3);
        }

        [Fact]
        public void PackBandTint_OutOfRangeIndexFallsBackToPaletteZero() {
            Assert.Equal(Theme.PackBandTint(0), Theme.PackBandTint(99));
        }
    }
}
