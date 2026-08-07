using System.Collections.Generic;
using System.Linq;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class PackApplyTests {
        static TacticsPack Pack(string id, params string[] presetIds) => new TacticsPack {
            Id = id, Name = "Pack " + id, PresetIds = new List<string>(presetIds),
        };

        static TacticsRule Linked(string presetId, string packId) => new TacticsRule {
            PresetId = presetId, PackId = packId,
        };

        // Every preset exists unless a test says otherwise.
        static bool AllExist(string presetId) => true;

        [Fact]
        public void PlanApply_on_empty_list_returns_one_linked_rule_per_member() {
            var plan = PackRegistry.PlanApply(Pack("A", "p1", "p2"), new List<TacticsRule>(), AllExist);

            Assert.Equal(2, plan.Count);
            Assert.Equal(new[] { "p1", "p2" }, plan.Select(r => r.PresetId));
            Assert.All(plan, r => Assert.Equal("A", r.PackId));
            Assert.All(plan, r => Assert.True(r.Enabled));
            Assert.All(plan, r => Assert.False(string.IsNullOrEmpty(r.Id)));
            // Linked rules must carry an empty body — PresetRegistry.Resolve supplies it.
            Assert.All(plan, r => Assert.Empty(r.ConditionGroups));
        }

        [Fact]
        public void PlanApply_preserves_member_order() {
            var plan = PackRegistry.PlanApply(Pack("A", "p3", "p1", "p2"), new List<TacticsRule>(), AllExist);
            Assert.Equal(new[] { "p3", "p1", "p2" }, plan.Select(r => r.PresetId));
        }

        [Fact]
        public void PlanApply_skips_members_already_present_from_the_same_pack() {
            var existing = new List<TacticsRule> { Linked("p1", "A") };
            var plan = PackRegistry.PlanApply(Pack("A", "p1", "p2"), existing, AllExist);

            Assert.Single(plan);
            Assert.Equal("p2", plan[0].PresetId);
        }

        [Fact]
        public void PlanApply_skips_a_member_already_present_from_another_pack() {
            // Preset-based dedup: one rule per preset per list, no matter which pack asks.
            // Prevents the duplicate spam from applying two packs that share members.
            var existing = new List<TacticsRule> { Linked("p1", "B") };
            var plan = PackRegistry.PlanApply(Pack("A", "p1", "p2"), existing, AllExist);

            Assert.Single(plan);
            Assert.Equal("p2", plan[0].PresetId);
        }

        [Fact]
        public void PlanApply_skips_a_member_already_present_as_a_hand_built_link() {
            var existing = new List<TacticsRule> { Linked("p1", null) };
            var plan = PackRegistry.PlanApply(Pack("A", "p1"), existing, AllExist);

            Assert.Empty(plan);
        }

        [Fact]
        public void PlanApply_ignores_rules_without_a_preset_link_when_deduping() {
            // A standalone rule has no PresetId; it can never be "the same rule" as a member.
            var existing = new List<TacticsRule> { new TacticsRule { Name = "hand-built" } };
            var plan = PackRegistry.PlanApply(Pack("A", "p1"), existing, AllExist);

            Assert.Single(plan);
        }

        [Fact]
        public void PlanApply_drops_members_whose_preset_is_gone() {
            var plan = PackRegistry.PlanApply(Pack("A", "p1", "ghost"), new List<TacticsRule>(),
                presetId => presetId != "ghost");

            Assert.Single(plan);
            Assert.Equal("p1", plan[0].PresetId);
        }

        [Fact]
        public void PlanApply_tolerates_null_and_empty_input() {
            Assert.Empty(PackRegistry.PlanApply(null, new List<TacticsRule>(), AllExist));
            Assert.Empty(PackRegistry.PlanApply(Pack("A"), null, AllExist));
            Assert.Empty(PackRegistry.PlanApply(Pack("A", "", null), null, AllExist));
        }

        [Fact]
        public void AppliedPackIds_returns_distinct_ids_in_first_appearance_order() {
            var rules = new List<TacticsRule> {
                Linked("p1", "B"), new TacticsRule(), Linked("p2", "A"), Linked("p3", "B"),
            };
            Assert.Equal(new[] { "B", "A" }, PackRegistry.AppliedPackIds(rules));
        }

        [Fact]
        public void AppliedPackIds_ignores_null_list_and_unpacked_rules() {
            Assert.Empty(PackRegistry.AppliedPackIds(null));
            Assert.Empty(PackRegistry.AppliedPackIds(new List<TacticsRule> { new TacticsRule() }));
        }

        [Fact]
        public void StripPreset_removes_the_member_and_reports_only_changed_packs() {
            var packs = new List<TacticsPack> { Pack("A", "p1", "p2"), Pack("B", "p3") };
            var changed = PackRegistry.StripPreset(packs, "p1");

            Assert.Single(changed);
            Assert.Equal("A", changed[0].Id);
            Assert.Equal(new[] { "p2" }, packs[0].PresetIds);
            Assert.Equal(new[] { "p3" }, packs[1].PresetIds);
        }

        [Fact]
        public void StripPreset_handles_duplicates_null_list_and_unknown_id() {
            var packs = new List<TacticsPack> { Pack("A", "p1", "p1") };
            Assert.Single(PackRegistry.StripPreset(packs, "p1"));
            Assert.Empty(packs[0].PresetIds);

            Assert.Empty(PackRegistry.StripPreset(packs, "nope"));
            Assert.Empty(PackRegistry.StripPreset(null, "p1"));
        }

        [Fact]
        public void CountAlreadyApplied_returns_zero_when_nothing_applied_yet() {
            var count = PackRegistry.CountAlreadyApplied(Pack("A", "p1", "p2"), new List<TacticsRule>());
            Assert.Equal(0, count);
        }

        [Fact]
        public void CountAlreadyApplied_counts_only_members_present_from_this_pack() {
            var existing = new List<TacticsRule> { Linked("p1", "A") };
            var count = PackRegistry.CountAlreadyApplied(Pack("A", "p1", "p2"), existing);
            Assert.Equal(1, count);
        }

        [Fact]
        public void CountAlreadyApplied_does_not_count_a_member_whose_preset_is_gone() {
            // "ghost" is a pack member whose preset was deleted outside the game — PlanApply
            // skips it for THAT reason, not because it's already present, and no rule for it
            // ever exists. The old `pack.PresetIds.Count - plan.Count` formula would have
            // reported 2 here (misattributing the unresolvable member as "already present");
            // the correct count is 1 — only the genuinely-applied "p1".
            var existing = new List<TacticsRule> { Linked("p1", "A") };
            var count = PackRegistry.CountAlreadyApplied(Pack("A", "p1", "ghost"), existing);
            Assert.Equal(1, count);
        }

        [Fact]
        public void CountAlreadyApplied_counts_a_member_present_from_any_pack() {
            // Must use the same membership rule as PlanApply, or added + already-present
            // stops adding up and the status message lies again.
            var rules = new List<TacticsRule> { Linked("p1", "B"), Linked("p2", null) };
            Assert.Equal(2, PackRegistry.CountAlreadyApplied(Pack("A", "p1", "p2"), rules));
        }

        [Fact]
        public void CountAlreadyApplied_counts_a_duplicate_member_id_once() {
            var existing = new List<TacticsRule> { Linked("p1", "A") };
            var count = PackRegistry.CountAlreadyApplied(Pack("A", "p1", "p1"), existing);
            Assert.Equal(1, count);
        }

        [Fact]
        public void CountAlreadyApplied_tolerates_null_pack_and_null_rule_list() {
            Assert.Equal(0, PackRegistry.CountAlreadyApplied(null, new List<TacticsRule>()));
            Assert.Equal(0, PackRegistry.CountAlreadyApplied(Pack("A", "p1"), null));
        }
    }
}
