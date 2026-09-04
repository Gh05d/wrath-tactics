using Kingmaker.UnitLogic.Commands.Base;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>
    /// Maps a rule's ActionType — plus, for ability-backed types, the resolved
    /// AbilityData.RuntimeActionType — onto the UnitCommand slot the rule occupies.
    /// Pure: no engine state, no side effects, fully unit-testable.
    ///
    /// UnitCommand.CommandType is Free = 0, Standard = 1, Swift = 2, Move = 3, and
    /// UnitCommands.m_Commands is indexed by it, so (int)slot doubles as a budget index.
    /// </summary>
    internal static class ActionSlots {
        internal static UnitCommand.CommandType? Classify(
            ActionType type, UnitCommand.CommandType? abilitySlot) {
            switch (type) {
                case ActionType.CastSpell:
                case ActionType.CastAbility:
                case ActionType.UseItem:
                case ActionType.Heal:
                    // RuntimeActionType already folds in Quicken (Swift -> Standard once the
                    // swift action is spent) and MythicAbilitiesAsMoveAction (Standard -> Move).
                    // An unresolvable slot degrades to Standard so a classification miss
                    // behaves like the old one-action-per-tick evaluator instead of escaping
                    // the priority gate.
                    return abilitySlot ?? UnitCommand.CommandType.Standard;

                case ActionType.AttackTarget:
                    return UnitCommand.CommandType.Standard;

                // ThrowSplash bypasses Commands.Run entirely (Rulebook.Trigger plus manual
                // stack consumption), so it occupies no engine slot. It still claims Standard
                // in the tick budget: a thrown flask IS a standard action, and leaving it
                // unclaimed would let it fire on top of a cast AND an attack every tick.
                case ActionType.ThrowSplash:
                    return UnitCommand.CommandType.Standard;

                // UnitSwitchHandEquipmentSet is CommandType.Free (IL-verified).
                case ActionType.SwitchWeaponSet:
                    return UnitCommand.CommandType.Free;

                // Sets ActivatableAbility.IsOn — issues no command, so it claims nothing and
                // is exempt from both the gate and the budget. A toggle rule stops matching
                // once its activatable reaches the requested state, so this does not spam.
                case ActionType.ToggleActivatable:
                    return null;

                // Claiming Standard is cosmetic — DoNothing hard-stops the whole tick — but
                // it keeps the classification total and honest.
                case ActionType.DoNothing:
                    return UnitCommand.CommandType.Standard;

                default:
                    return UnitCommand.CommandType.Standard;
            }
        }

        /// <summary>
        /// True for the slot the ActiveRuleTracker priority gate governs. Only Standard-slot
        /// rules participate in DAO-style preemption; move/swift/free rules bypass the gate.
        /// </summary>
        internal static bool IsGated(UnitCommand.CommandType? slot) {
            return slot == UnitCommand.CommandType.Standard;
        }
    }
}
