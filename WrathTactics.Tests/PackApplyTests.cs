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
        public void PlanApply_still_adds_a_member_present_only_from_another_pack() {
            // Two packs sharing a preset must each own their own copy — removing pack B
            // must not strip a rule that pack A also asked for.
            var existing = new List<TacticsRule> { Linked("p1", "B") };
            var plan = PackRegistry.PlanApply(Pack("A", "p1"), existing, AllExist);

            Assert.Single(plan);
            Assert.Equal("A", plan[0].PackId);
        }

        [Fact]
        public void PlanApply_still_adds_a_member_present_as_a_hand_built_link() {
            var existing = new List<TacticsRule> { Linked("p1", null) };
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
    }
}
