using Kingmaker.UnitLogic.Commands.Base;
using WrathTactics.Engine;
using WrathTactics.Models;
using Xunit;

namespace WrathTactics.Tests {
    public class ActionSlotsTests {
        [Theory]
        [InlineData(ActionType.CastSpell)]
        [InlineData(ActionType.CastAbility)]
        [InlineData(ActionType.UseItem)]
        [InlineData(ActionType.Heal)]
        public void ability_backed_types_use_the_supplied_ability_slot(ActionType type) {
            Assert.Equal(UnitCommand.CommandType.Swift,
                ActionSlots.Classify(type, UnitCommand.CommandType.Swift));
            Assert.Equal(UnitCommand.CommandType.Move,
                ActionSlots.Classify(type, UnitCommand.CommandType.Move));
            Assert.Equal(UnitCommand.CommandType.Free,
                ActionSlots.Classify(type, UnitCommand.CommandType.Free));
            Assert.Equal(UnitCommand.CommandType.Standard,
                ActionSlots.Classify(type, UnitCommand.CommandType.Standard));
        }

        [Theory]
        [InlineData(ActionType.CastSpell)]
        [InlineData(ActionType.CastAbility)]
        [InlineData(ActionType.UseItem)]
        [InlineData(ActionType.Heal)]
        public void ability_backed_types_fall_back_to_standard_when_slot_unknown(ActionType type) {
            Assert.Equal(UnitCommand.CommandType.Standard, ActionSlots.Classify(type, null));
        }

        [Theory]
        [InlineData(ActionType.AttackTarget)]
        [InlineData(ActionType.ThrowSplash)]
        [InlineData(ActionType.DoNothing)]
        public void fixed_standard_types_ignore_the_supplied_slot(ActionType type) {
            Assert.Equal(UnitCommand.CommandType.Standard, ActionSlots.Classify(type, null));
            Assert.Equal(UnitCommand.CommandType.Standard,
                ActionSlots.Classify(type, UnitCommand.CommandType.Move));
        }

        [Fact]
        public void switch_weapon_set_is_a_free_action() {
            Assert.Equal(UnitCommand.CommandType.Free,
                ActionSlots.Classify(ActionType.SwitchWeaponSet, null));
            Assert.Equal(UnitCommand.CommandType.Free,
                ActionSlots.Classify(ActionType.SwitchWeaponSet, UnitCommand.CommandType.Standard));
        }

        [Fact]
        public void toggle_activatable_claims_no_slot() {
            Assert.Null(ActionSlots.Classify(ActionType.ToggleActivatable, null));
            Assert.Null(ActionSlots.Classify(ActionType.ToggleActivatable,
                UnitCommand.CommandType.Standard));
        }

        [Fact]
        public void unknown_action_type_degrades_to_standard() {
            Assert.Equal(UnitCommand.CommandType.Standard, ActionSlots.Classify((ActionType)999, null));
        }

        [Fact]
        public void only_standard_is_gated() {
            Assert.True(ActionSlots.IsGated(UnitCommand.CommandType.Standard));
            Assert.False(ActionSlots.IsGated(UnitCommand.CommandType.Free));
            Assert.False(ActionSlots.IsGated(UnitCommand.CommandType.Swift));
            Assert.False(ActionSlots.IsGated(UnitCommand.CommandType.Move));
            Assert.False(ActionSlots.IsGated(null));
        }

        // --- IssuesAnimatedCommand -------------------------------------------------------

        [Theory]
        [InlineData(ActionType.CastSpell, true)]
        [InlineData(ActionType.CastAbility, true)]
        [InlineData(ActionType.UseItem, true)]
        [InlineData(ActionType.Heal, true)]
        [InlineData(ActionType.AttackTarget, true)]
        [InlineData(ActionType.ThrowSplash, false)]   // Rulebook.Trigger, no command
        [InlineData(ActionType.SwitchWeaponSet, false)]
        [InlineData(ActionType.ToggleActivatable, false)]
        [InlineData(ActionType.DoNothing, false)]
        public void animated_command_types(ActionType type, bool expected) {
            Assert.Equal(expected, ActionSlots.IssuesAnimatedCommand(type));
        }

        // --- CheckConflict ---------------------------------------------------------------

        const UnitCommand.CommandType Std = UnitCommand.CommandType.Standard;
        const UnitCommand.CommandType Swf = UnitCommand.CommandType.Swift;
        const UnitCommand.CommandType Mov = UnitCommand.CommandType.Move;
        const UnitCommand.CommandType Fre = UnitCommand.CommandType.Free;

        [Theory]
        [InlineData(Swf, Std)]
        [InlineData(Std, Mov)]
        [InlineData(Std, Swf)]
        [InlineData(Mov, Swf)]
        [InlineData(Swf, Mov)]
        [InlineData(Fre, Std)]
        public void a_started_occupant_always_conflicts(UnitCommand.CommandType issuing, UnitCommand.CommandType occupied) {
            // Even with a generous Standard cooldown and a free issuing slot.
            Assert.Equal(SlotConflict.Running,
                ActionSlots.CheckConflict(issuing, occupied, occupantStarted: true, occupantApproaching: false, occupantOwn: true, occupantIsCast: true,
                    issuingSlotOnCooldown: false, standardCooldownRemaining: 5f));
        }

        [Theory]
        [InlineData(false, 0f)]
        [InlineData(false, 5f)]   // the 1.29.1-rc "safe overlap" — Run(Move) deletes the pending cast regardless
        [InlineData(true, 5f)]
        public void move_never_issues_over_an_own_standard_command(bool started, float cooldown) {
            Assert.Equal(SlotConflict.PairedOwn,
                ActionSlots.CheckConflict(Mov, Std, occupantStarted: started, occupantApproaching: false, occupantOwn: true, occupantIsCast: true,
                    issuingSlotOnCooldown: false, standardCooldownRemaining: cooldown));
        }

        [Theory]
        [InlineData(false, false, true)]   // pending default-action cast: AI re-issues it after our Move
        [InlineData(false, true, true)]    // pending and still approaching
        [InlineData(false, false, false)]  // pending auto-attack
        [InlineData(true, false, false)]   // running auto-attack: interrupting it is what a click does
        public void move_may_cancel_a_pending_engine_command_or_an_auto_attack(bool started, bool approaching, bool isCast) {
            Assert.Equal(SlotConflict.None,
                ActionSlots.CheckConflict(Mov, Std, occupantStarted: started, occupantApproaching: approaching, occupantOwn: false, occupantIsCast: isCast,
                    issuingSlotOnCooldown: true, standardCooldownRemaining: 0f));
        }

        [Fact]
        public void move_waits_for_a_running_foreign_cast() {
            // Run(Move) over a casting unit queues behind the cast and flags it
            // InterruptAsSoonAsPossible — the cast dies in its wind-up. Two seconds of
            // patience instead.
            Assert.Equal(SlotConflict.Running,
                ActionSlots.CheckConflict(Mov, Std, occupantStarted: true, occupantApproaching: false, occupantOwn: false, occupantIsCast: true,
                    issuingSlotOnCooldown: false, standardCooldownRemaining: 0f));
        }

        [Fact]
        public void swift_needs_the_standard_cooldown_to_outlast_its_animation() {
            Assert.Equal(SlotConflict.None,
                ActionSlots.CheckConflict(Swf, Std, occupantStarted: false, occupantApproaching: false, occupantOwn: true, occupantIsCast: true,
                    issuingSlotOnCooldown: false, standardCooldownRemaining: 3f));
            Assert.Equal(SlotConflict.Pending,
                ActionSlots.CheckConflict(Swf, Std, occupantStarted: false, occupantApproaching: false, occupantOwn: true, occupantIsCast: true,
                    issuingSlotOnCooldown: false, standardCooldownRemaining: 1f));
            Assert.Equal(SlotConflict.Pending,
                ActionSlots.CheckConflict(Swf, Std, occupantStarted: false, occupantApproaching: false, occupantOwn: true, occupantIsCast: true,
                    issuingSlotOnCooldown: true, standardCooldownRemaining: 5f));
        }

        [Theory]
        [InlineData(Std, Mov)]   // pending Move ignores a running Standard → would cut our cast
        [InlineData(Std, Swf)]
        [InlineData(Mov, Swf)]
        [InlineData(Swf, Mov)]
        [InlineData(Fre, Std)]
        [InlineData(Fre, Mov)]
        public void other_pending_combinations_always_wait(UnitCommand.CommandType issuing, UnitCommand.CommandType occupied) {
            Assert.Equal(SlotConflict.Pending,
                ActionSlots.CheckConflict(issuing, occupied, occupantStarted: false, occupantApproaching: false, occupantOwn: true, occupantIsCast: true,
                    issuingSlotOnCooldown: false, standardCooldownRemaining: 5f));
        }

        [Theory]
        [InlineData(Swf)]
        [InlineData(Fre)]
        public void a_pending_standard_that_is_still_approaching_its_target_conflicts(UnitCommand.CommandType issuing) {
            // UnitCommands.Run interrupts every unstarted command that is not yet close
            // enough to its target when a Move (or a far-targeted Swift) is issued. The
            // cooldown is irrelevant: the approach, not the cooldown, is what holds it.
            Assert.Equal(SlotConflict.Approaching,
                ActionSlots.CheckConflict(issuing, Std, occupantStarted: false, occupantApproaching: true, occupantOwn: true, occupantIsCast: true,
                    issuingSlotOnCooldown: false, standardCooldownRemaining: 5f));
        }

        [Fact]
        public void started_wins_over_approaching() {
            Assert.Equal(SlotConflict.Running,
                ActionSlots.CheckConflict(Swf, Std, occupantStarted: true, occupantApproaching: true, occupantOwn: true, occupantIsCast: true,
                    issuingSlotOnCooldown: false, standardCooldownRemaining: 5f));
        }

        [Fact]
        public void move_to_target_is_a_move_slot_command_that_needs_the_cross_slot_check() {
            Assert.Equal(UnitCommand.CommandType.Move, ActionSlots.Classify(ActionType.MoveToTarget, null));
            Assert.Equal(UnitCommand.CommandType.Move, ActionSlots.Classify(ActionType.MoveToTarget, UnitCommand.CommandType.Standard));
            Assert.True(ActionSlots.NeedsCrossSlotCheck(ActionType.MoveToTarget));
            Assert.False(ActionSlots.IssuesAnimatedCommand(ActionType.MoveToTarget));
            Assert.False(ActionSlots.IsGated(ActionSlots.Classify(ActionType.MoveToTarget, null)));
        }

        [Theory]
        [InlineData(2.5f, RangeBracket.Melee, true)]
        [InlineData(2.0f, RangeBracket.Melee, false)]
        [InlineData(10.1f, RangeBracket.Short, true)]
        [InlineData(19.9f, RangeBracket.Medium, false)]
        [InlineData(35f, RangeBracket.Far, true)]
        [InlineData(35f, RangeBracket.Long, false)]
        public void beyond_bracket_means_the_walk_still_has_ground_to_cover(float distance, RangeBracket bracket, bool expected) {
            Assert.Equal(expected, RangeBrackets.Beyond(distance, bracket));
        }

        // --- ActionSpent (engine action budget) ---------------------------------------

        [Theory]
        [InlineData(4.0f, 3.0f)]   // spent, and the next tick comes before it frees
        [InlineData(3.1f, 3.0f)]
        public void an_action_still_cooling_past_the_next_tick_is_spent(float remaining, float tick) {
            Assert.True(ActionSlots.ActionSpent(slotOnCooldown: true, remainingSeconds: remaining, tickIntervalSeconds: tick));
        }

        [Theory]
        [InlineData(2.0f, 3.0f)]   // frees before the next tick: buffer it so it starts on time
        [InlineData(3.0f, 3.0f)]   // boundary counts as "starts before the next tick"
        [InlineData(0.0f, 3.0f)]
        public void an_action_that_frees_before_the_next_tick_may_be_buffered(float remaining, float tick) {
            Assert.False(ActionSlots.ActionSpent(slotOnCooldown: true, remainingSeconds: remaining, tickIntervalSeconds: tick));
        }

        [Fact]
        public void walks_bypass_the_action_budget_every_other_type_uses_it() {
            Assert.False(ActionSlots.UsesActionBudget(ActionType.MoveToTarget));
            Assert.True(ActionSlots.UsesActionBudget(ActionType.CastSpell));
            Assert.True(ActionSlots.UsesActionBudget(ActionType.AttackTarget));
            Assert.True(ActionSlots.UsesActionBudget(ActionType.CastAbility));
        }

        [Fact]
        public void a_slot_not_on_cooldown_is_never_spent_whatever_the_number_says() {
            // HasCooldownForCommand is the engine's verdict; the float is only used for the
            // buffering tolerance.
            Assert.False(ActionSlots.ActionSpent(slotOnCooldown: false, remainingSeconds: 9f, tickIntervalSeconds: 3f));
        }

        [Fact]
        public void swift_margin_covers_a_quick_cast_animation() {
            Assert.True(ActionSlots.SwiftOverlapMinStandardCooldown >= 2f);
        }
    }
}
