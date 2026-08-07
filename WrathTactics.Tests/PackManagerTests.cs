using System.Collections.Generic;
using System.IO;
using WrathTactics.Models;
using WrathTactics.Persistence;
using Xunit;

namespace WrathTactics.Tests {
    public class PackManagerTests {
        static string TempDir() {
            var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            return dir;
        }

        static TacticsPack Sample(string name, params string[] presetIds) => new TacticsPack {
            Name = name,
            ColorIndex = 2,
            PresetIds = new List<string>(presetIds),
        };

        [Fact]
        public void Save_then_load_roundtrips_all_fields() {
            var dir = TempDir();
            try {
                var pack = Sample("Seelah Melee", "p1", "p2");
                Assert.True(PackManager.SaveTo(dir, pack));

                var loaded = PackManager.LoadAllFrom(dir);
                Assert.Single(loaded);
                Assert.Equal(pack.Id, loaded[0].Id);
                Assert.Equal("Seelah Melee", loaded[0].Name);
                Assert.Equal(2, loaded[0].ColorIndex);
                Assert.Equal(new[] { "p1", "p2" }, loaded[0].PresetIds);
            } finally {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Load_sorts_by_name_case_insensitively() {
            var dir = TempDir();
            try {
                PackManager.SaveTo(dir, Sample("zebra"));
                PackManager.SaveTo(dir, Sample("Alpha"));
                var loaded = PackManager.LoadAllFrom(dir);
                Assert.Equal(new[] { "Alpha", "zebra" }, loaded.ConvertAll(p => p.Name));
            } finally {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Load_skips_unparsable_file_and_keeps_the_rest() {
            var dir = TempDir();
            try {
                PackManager.SaveTo(dir, Sample("Good"));
                File.WriteAllText(Path.Combine(dir, "broken.json"), "{ this is not json");
                var loaded = PackManager.LoadAllFrom(dir);
                Assert.Single(loaded);
                Assert.Equal("Good", loaded[0].Name);
            } finally {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Load_of_missing_keys_retains_property_initializers() {
            var dir = TempDir();
            try {
                File.WriteAllText(Path.Combine(dir, "legacy.json"), "{\"Name\":\"Legacy\"}");
                var loaded = PackManager.LoadAllFrom(dir);
                Assert.Single(loaded);
                Assert.False(string.IsNullOrEmpty(loaded[0].Id));
                Assert.NotNull(loaded[0].PresetIds);
                Assert.Empty(loaded[0].PresetIds);
            } finally {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Load_repairs_explicit_null_id_and_null_member_list() {
            var dir = TempDir();
            try {
                File.WriteAllText(Path.Combine(dir, "legacy.json"), "{\"Name\":\"Legacy\",\"Id\":null,\"PresetIds\":null}");
                var loaded = PackManager.LoadAllFrom(dir);
                Assert.Single(loaded);
                Assert.False(string.IsNullOrEmpty(loaded[0].Id));
                Assert.NotNull(loaded[0].PresetIds);
                Assert.Empty(loaded[0].PresetIds);
            } finally {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Delete_removes_the_file_and_is_idempotent() {
            var dir = TempDir();
            try {
                var pack = Sample("Doomed");
                PackManager.SaveTo(dir, pack);
                Assert.True(PackManager.DeleteFrom(dir, pack.Id));
                Assert.Empty(PackManager.LoadAllFrom(dir));
                Assert.True(PackManager.DeleteFrom(dir, pack.Id));
            } finally {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Save_rejects_null_pack_and_empty_id() {
            var dir = TempDir();
            try {
                Assert.False(PackManager.SaveTo(dir, null));
                Assert.False(PackManager.SaveTo(dir, new TacticsPack { Id = "" }));
                Assert.Empty(PackManager.LoadAllFrom(dir));
            } finally {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void Load_from_missing_directory_returns_empty() {
            var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Assert.Empty(PackManager.LoadAllFrom(dir));
        }
    }
}
