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

        /// <summary>
        /// True for rule types whose execution issues an animated UnitCommand
        /// (UnitUseAbility or UnitAttack). Only these can collide on the unit's
        /// AnimationManager — see CheckConflict.
        /// </summary>
        internal static bool IssuesAnimatedCommand(ActionType type) {
            switch (type) {
                case ActionType.CastSpell:
                case ActionType.CastAbility:
                case ActionType.UseItem:
                case ActionType.Heal:
                case ActionType.AttackTarget:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// A running Swift does NOT hold a pending Standard back (the engine's Standard start
        /// check only looks at a running Move). So a Swift may overlap a pending Standard only
        /// when the Standard is guaranteed to still be cooling down after the swift animation
        /// has finished. 2.5 s comfortably covers every quick-cast animation.
        /// </summary>
        internal const float SwiftOverlapMinStandardCooldown = 2.5f;

        /// <summary>
        /// Decides whether issuing a command in <paramref name="issuing"/> would destroy or be
        /// destroyed by an unfinished animated command (UnitUseAbility / UnitAttack) currently
        /// sitting in <paramref name="occupied"/>. The caller guarantees the two slots differ
        /// and the occupant is animated and not finished; <paramref name="occupantApproaching"/>
        /// is <c>!occupant.IsStarted &amp;&amp; !occupant.IsUnitCloseEnough()</c>,
        /// <paramref name="occupantOwn"/> is <c>PlayerCommandGuard.IsOurs(occupant)</c>.
        /// Same-slot conflicts are the budget's and the priority gate's business.
        ///
        /// Two engine facts drive this (both IL-verified, both learnt the hard way in 1.29.0):
        ///
        /// 1. <b>Move and Standard are a PAIR, not independent slots.</b>
        ///    <c>UnitCommands.Run(cmd)</c> calls <c>InterruptAndRemoveCommand(cmd.Type)</c>,
        ///    whose one-argument overload also removes the paired slot: a Move command
        ///    interrupts whatever sits in Standard (pending or running) and vice versa.
        ///    So a Move ability issued over our own pending cast simply deletes the cast —
        ///    no cooldown margin makes that safe. Over an ENGINE-issued Standard command
        ///    (auto-attack, the unit's default action) it is exactly what clicking the
        ///    ability does: the engine's command dies, ours runs, the party AI re-issues
        ///    its own afterwards. Player casts never reach this point — PlayerCommandGuard
        ///    skips the whole unit while one is in the slot or the queue.
        ///
        /// 2. <b>Two animated commands must never run at once.</b> Slots tick in array order
        ///    and each unstarted command starts as soon as its own cooldown allows; two
        ///    running commands share one AnimationManager, the newer animation releases the
        ///    older one and <c>UnitCommand.Tick</c> interrupts it as "done but not acted".
        ///    The engine's only cross-slot hold is Standard-waits-while-Move-IsRunning.
        /// </summary>
        internal static SlotConflict CheckConflict(
            UnitCommand.CommandType issuing,
            UnitCommand.CommandType occupied,
            bool occupantStarted,
            bool occupantApproaching,
            bool occupantOwn,
            bool issuingSlotOnCooldown,
            float standardCooldownRemaining) {
            // Fact 1: Run(Move) removes the Standard command outright.
            if (issuing == UnitCommand.CommandType.Move && occupied == UnitCommand.CommandType.Standard) {
                return occupantOwn ? SlotConflict.PairedOwn : SlotConflict.None;
            }

            // Fact 2: a started, unfinished animated command owns the AnimationManager.
            // (Also covers Standard-over-Move: Run(Standard) removes the Move command, and
            // an own Move ability in flight must be allowed to finish.)
            if (occupantStarted) return SlotConflict.Running;

            // A pending command that is still walking towards its target is held by
            // distance, not by a cooldown. Run(cmd) interrupts every unstarted command with
            // !IsUnitCloseEnough() whenever cmd is Standard or Move (Swift/Free only when cmd
            // itself is out of reach), so issuing now cancels that approach.
            if (occupantApproaching) return SlotConflict.Approaching;

            // Pending Standard occupant, Swift issuing: Swift is not paired with Standard,
            // but nothing holds Standard back from a running Swift either, so the Standard
            // cooldown must outlast the swift animation and our Swift must start this frame.
            if (issuing == UnitCommand.CommandType.Swift
                && occupied == UnitCommand.CommandType.Standard
                && !issuingSlotOnCooldown
                && standardCooldownRemaining > SwiftOverlapMinStandardCooldown) {
                return SlotConflict.None;
            }

            // Every other pending combination either starts in the same frame as ours
            // (Standard ticks before Swift/Move, so a cooldown-free pending Standard starts
            // first and our command lands on top of it) or starts later, underneath our
            // running command (a pending Move/Swift ignores a running Standard). Wait a tick.
            return SlotConflict.Pending;
        }
    }

    /// <summary>Outcome of ActionSlots.CheckConflict.</summary>
    internal enum SlotConflict {
        None,
        /// <summary>An animated command in another slot has started and not finished.</summary>
        Running,
        /// <summary>An animated command in another slot is waiting to start and would
        /// collide with ours once either of them starts.</summary>
        Pending,
        /// <summary>An animated command in another slot has not started because its
        /// executor is still walking into range; issuing ours would make
        /// UnitCommands.Run cancel that approach.</summary>
        Approaching,
        /// <summary>Our own command sits in the slot paired with the issuing one
        /// (Move ↔ Standard); UnitCommands.Run would remove it outright.</summary>
        PairedOwn,
    }
}
