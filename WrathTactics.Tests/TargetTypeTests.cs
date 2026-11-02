using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    // TargetType is persisted by numeric index in preset/config JSON: members are append-only.
    public class TargetTypeTests {
        [Theory]
        [InlineData(TargetType.ConditionTarget, 17)]
        [InlineData(TargetType.PointAtSelf, 20)]
        [InlineData(TargetType.PointAtConditionTarget, 21)]
        [InlineData(TargetType.SpecificAlly, 22)]
        [InlineData(TargetType.AllyMostAllyNeighbors, 24)]
        [InlineData(TargetType.ConditionTargetAlly, 25)]
        public void Persisted_indices_are_pinned(TargetType type, int expected) {
            Assert.Equal(expected, (int)type);
        }

        [Theory]
        [InlineData("en_GB")]
        [InlineData("de_DE")]
        [InlineData("fr_FR")]
        [InlineData("ru_RU")]
        [InlineData("zh_CN")]
        public void ConditionTargetAlly_is_localized(string locale) {
            var pack = LoadLocale(locale);
            const string key = "enum.target.ConditionTargetAlly";
            Assert.True(pack.TryGetValue(key, out var value), $"{locale} missing {key}");
            Assert.False(string.IsNullOrWhiteSpace(value), $"{locale} empty {key}");
        }

        static Dictionary<string, string> LoadLocale(string locale) {
            var assembly = typeof(TargetType).Assembly;
            using var stream = assembly.GetManifestResourceStream($"WrathTactics.Localization.{locale}.json");
            Assert.NotNull(stream);
            using var reader = new StreamReader(stream);
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(reader.ReadToEnd());
        }
    }
}
