using System.Collections.Generic;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class ActiveRuleTrackerTests {
        static TacticsRule R(string id) => new TacticsRule { Id = id };

        static List<TacticsRule> List(params string[] ids) {
            var list = new List<TacticsRule>(ids.Length);
            foreach (var id in ids) list.Add(R(id));
            return list;
        }

        [Fact]
        public void active_in_globals_middle_gates_globals_below_and_skips_chars() {
            var globals = List("g0", "g1", "g2", "g3");
            var chars   = List("c0", "c1");

            var res = ActiveRuleTracker.Resolve(RuleListSource.Global, "g2", globals, chars);

            Assert.False(res.Stale);
            Assert.Equal(2, res.GlobalGate);   // i < 2  => g0, g1 only
            Assert.Equal(0, res.CharGate);     // i < 0  => no chars at all
        }

        [Fact]
        public void active_in_globals_top_blocks_everything() {
            var globals = List("g0", "g1");
            var chars   = List("c0");

            var res = ActiveRuleTracker.Resolve(RuleListSource.Global, "g0", globals, chars);

            Assert.False(res.Stale);
            Assert.Equal(0, res.GlobalGate);   // nothing higher than g0
            Assert.Equal(0, res.CharGate);
        }

        [Fact]
        public void active_in_chars_lets_all_globals_run_and_gates_chars_below() {
            var globals = List("g0", "g1");
            var chars   = List("c0", "c1", "c2");

            var res = ActiveRuleTracker.Resolve(RuleListSource.Character, "c1", globals, chars);

            Assert.False(res.Stale);
            Assert.Equal(int.MaxValue, res.GlobalGate);  // no gate on globals
            Assert.Equal(1, res.CharGate);               // only c0
        }

        [Fact]
        public void active_in_chars_top_lets_all_globals_run_and_blocks_chars_below() {
            var globals = List("g0");
            var chars   = List("c0", "c1");

            var res = ActiveRuleTracker.Resolve(RuleListSource.Character, "c0", globals, chars);

            Assert.False(res.Stale);
            Assert.Equal(int.MaxValue, res.GlobalGate);
            Assert.Equal(0, res.CharGate);
        }

        [Fact]
        public void active_id_missing_from_globals_is_stale() {
            var globals = List("g0", "g1");
            var chars   = List("c0");

            var res = ActiveRuleTracker.Resolve(RuleListSource.Global, "ghost", globals, chars);

            Assert.True(res.Stale);
        }

        [Fact]
        public void active_id_missing_from_chars_is_stale() {
            var globals = List("g0");
            var chars   = List("c0", "c1");

            var res = ActiveRuleTracker.Resolve(RuleListSource.Character, "ghost", globals, chars);

            Assert.True(res.Stale);
        }

        [Fact]
        public void empty_lists_with_global_lookup_are_stale() {
            var globals = new List<TacticsRule>();
            var chars   = new List<TacticsRule>();

            var res = ActiveRuleTracker.Resolve(RuleListSource.Global, "g0", globals, chars);

            Assert.True(res.Stale);
        }

        [Fact]
        public void active_in_globals_does_not_use_chars_id_collision() {
            // Sanity: an entry-id only present in chars must not match when activeSource = Global.
            var globals = List("g0");
            var chars   = List("c0", "g0");

            var res = ActiveRuleTracker.Resolve(RuleListSource.Global, "c0", globals, chars);

            Assert.True(res.Stale);
        }
    }
}
