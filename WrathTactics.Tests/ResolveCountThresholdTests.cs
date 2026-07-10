using System.Collections.Generic;
using System.Linq;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class ResolveCountThresholdTests {
        static List<Condition> Rows(params string[] value2s) =>
            value2s.Select(v => new Condition { Subject = ConditionSubject.EnemyCount, Value2 = v }).ToList();

        [Fact]
        public void Explicit_zero_is_honored() {
            Assert.Equal(0f, ConditionEvaluator.ResolveCountThreshold(Rows("0")));
        }

        [Theory]
        [InlineData("")]          // empty field
        [InlineData("abc")]       // garbage
        [InlineData("-1")]        // negative: rejected, would make >= gates always-true
        [InlineData("NaN")]       // parses but non-finite: rejected
        [InlineData("Infinity")]  // parses but non-finite: rejected
        [InlineData("-Infinity")] // parses but non-finite: rejected
        public void Unusable_values_default_to_one(string raw) {
            Assert.Equal(1f, ConditionEvaluator.ResolveCountThreshold(Rows(raw)));
        }

        [Theory]
        [InlineData("-1", "2", 2f)]  // rejected negative must not shadow a valid row
        [InlineData("NaN", "3", 3f)] // rejected NaN must not poison the max fold
        public void Rejected_values_do_not_shadow_valid_rows(string bad, string good, float expected) {
            Assert.Equal(expected, ConditionEvaluator.ResolveCountThreshold(Rows(bad, good)));
        }

        [Fact]
        public void Null_value2_defaults_to_one() {
            Assert.Equal(1f, ConditionEvaluator.ResolveCountThreshold(Rows(new string[] { null })));
        }

        [Fact]
        public void Single_value_passes_through() {
            Assert.Equal(3f, ConditionEvaluator.ResolveCountThreshold(Rows("3")));
        }

        [Fact]
        public void Multiple_rows_take_max() {
            Assert.Equal(3f, ConditionEvaluator.ResolveCountThreshold(Rows("2", "3")));
        }

        [Fact]
        public void Parsed_zero_beats_unparsable_default() {
            // A row with an explicit 0 must not be dragged up to 1 by an empty sibling row.
            Assert.Equal(0f, ConditionEvaluator.ResolveCountThreshold(Rows("0", "")));
        }

        [Fact]
        public void Zero_and_two_take_max() {
            Assert.Equal(2f, ConditionEvaluator.ResolveCountThreshold(Rows("0", "2")));
        }

        [Fact]
        public void No_rows_defaults_to_one() {
            Assert.Equal(1f, ConditionEvaluator.ResolveCountThreshold(Rows()));
        }
    }
}
