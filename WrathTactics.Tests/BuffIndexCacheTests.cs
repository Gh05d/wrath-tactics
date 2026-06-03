using System.Collections.Generic;
using System.IO;
using WrathTactics.Engine;
using Xunit;

namespace WrathTactics.Tests {
    public class BuffIndexCacheTests {
        static List<BuffBlueprintProvider.BuffEntry> Sample() => new List<BuffBlueprintProvider.BuffEntry> {
            new BuffBlueprintProvider.BuffEntry { Guid = "g1", Name = "Bless", InternalName = "BlessBuff" },
            new BuffBlueprintProvider.BuffEntry { Guid = "g2", Name = "Bane",  InternalName = "BaneBuff" },
        };

        [Fact]
        public void Roundtrip_with_matching_stamp_returns_entries() {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
            try {
                Assert.True(BuffIndexCache.SaveTo(path, "2.5.1", "enGB", Sample()));
                Assert.True(BuffIndexCache.TryLoadFrom(path, "2.5.1", "enGB", out var loaded));
                Assert.Equal(2, loaded.Count);
                Assert.Equal("Bless", loaded[0].Name);
                Assert.Equal("g1", loaded[0].Guid);
                Assert.Equal("BaneBuff", loaded[1].InternalName);
            } finally {
                File.Delete(path);
            }
        }

        [Theory]
        [InlineData("2.6.0", "enGB")]  // version drift (game patch)
        [InlineData("2.5.1", "deDE")]  // locale change (names are localized)
        public void Stamp_mismatch_rejects_cache(string version, string locale) {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
            try {
                BuffIndexCache.SaveTo(path, "2.5.1", "enGB", Sample());
                Assert.False(BuffIndexCache.TryLoadFrom(path, version, locale, out var loaded));
                Assert.Null(loaded);
            } finally {
                File.Delete(path);
            }
        }

        [Fact]
        public void Missing_file_returns_false() {
            var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
            Assert.False(BuffIndexCache.TryLoadFrom(path, "v", "l", out var loaded));
            Assert.Null(loaded);
        }
    }
}
