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
    }
}
