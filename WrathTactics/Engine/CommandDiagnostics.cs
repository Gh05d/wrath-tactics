using System;
using System.Diagnostics;
using System.Text;
using HarmonyLib;
using Kingmaker;
using Kingmaker.PubSubSystem;
using Kingmaker.UnitLogic.Commands;
using Kingmaker.UnitLogic.Commands.Base;
using WrathTactics.Logging;

namespace WrathTactics.Engine {
    /// <summary>
    /// Frame-accurate lifecycle trace for animated commands (UnitUseAbility / UnitAttack) on
    /// player-faction units. The evaluator only samples slot state once per tick, which is
    /// far too coarse to see WHO ended a command — the v1.29.x animation collisions and the
    /// "cast ended: Interrupt, default action took the slot" pattern were both invisible at
    /// tick granularity. Start / act / end come from the EventBus; Interrupt is a Harmony
    /// prefix because the engine raises no event for it and the caller is the whole point.
    /// Everything is DEBUG except interrupts of commands we issued (INFO).
    /// </summary>
    public class CommandDiagnostics : IUnitCommandStartHandler, IUnitCommandActHandler, IUnitCommandEndHandler {
        public void HandleUnitCommandDidStart(UnitCommand command) {
            if (!Relevant(command)) return;
            Log.Engine.Debug($"{Who(command)}: {Describe(command)} started{OthersRunning(command)}");
        }

        public void HandleUnitCommandDidAct(UnitCommand command) {
            if (!Relevant(command)) return;
            Log.Engine.Debug($"{Who(command)}: {Describe(command)} acted");
        }

        public void HandleUnitCommandDidEnd(UnitCommand command) {
            if (!Relevant(command)) return;
            Log.Engine.Debug($"{Who(command)}: {Describe(command)} ended {command.Result} (acted={command.IsActed})");
            // One of ours just freed a slot: evaluate next frame, before the party AI's
            // default action can claim it for the rest of the round.
            if (PlayerCommandGuard.IsOurs(command.Executor, command) && command.Executor.IsInCombat)
                TacticsEvaluator.RequestTick($"{Who(command)} {Describe(command)} ended");
        }

        internal static bool Relevant(UnitCommand command) {
            if (command == null) return false;
            if (!(command is UnitUseAbility) && !(command is UnitAttack) && !(command is UnitMoveTo)) return false;
            var unit = command.Executor;
            return unit != null && unit.IsPlayerFaction;
        }

        static string Who(UnitCommand command) {
            return command.Executor?.CharacterName ?? "?";
        }

        internal static string Describe(UnitCommand command) {
            string what = command is UnitUseAbility ua ? (ua.Ability?.Name ?? "UnitUseAbility") : command.GetType().Name;
            string owner = PlayerCommandGuard.IsOurs(command.Executor, command) ? "own" : "foreign";
            return $"{owner} {what} [{command.Type}]";
        }

        static string OthersRunning(UnitCommand command) {
            var slots = command.Executor?.Commands?.Raw;
            if (slots == null) return "";
            StringBuilder sb = null;
            for (int i = 0; i < slots.Length; i++) {
                var other = slots[i];
                if (other == null || ReferenceEquals(other, command) || !other.IsStarted || other.IsFinished) continue;
                if (sb == null) sb = new StringBuilder("; also running: ");
                else sb.Append(", ");
                sb.Append(Describe(other));
            }
            return sb?.ToString() ?? "";
        }

        // The three interrupt sites inside UnitCommandController.TickCommand are
        // ShouldBeInterrupted, InterruptAsSoonAsPossible && IsInterruptible, and
        // !CanAct / (UnitUseAbility && !CanCast) — dump exactly those inputs so a
        // TickCommand interrupt is attributable without another deck round.
        static string State(UnitCommand cmd) {
            try {
                var state = cmd.Executor?.State;
                var queue = cmd.Executor?.Commands?.Queue;
                return $", asap={cmd.InterruptAsSoonAsPossible}, interruptible={cmd.IsInterruptible}, shouldBeInterrupted={cmd.ShouldBeInterrupted}, canAct={state?.CanAct}, canCast={state?.CanCast}, closeEnough={cmd.IsUnitCloseEnough()}, queued={queue?.Count ?? 0}";
            } catch (Exception ex) {
                return $", state? ({ex.GetType().Name})";
            }
        }

        /// <summary>Top engine/mod frames of the current stack, one line, for "who interrupted us".</summary>
        internal static string CallerSummary(int maxFrames) {
            var trace = new StackTrace(2, false);
            var sb = new StringBuilder();
            int taken = 0;
            string previous = null;
            for (int i = 0; i < trace.FrameCount && taken < maxFrames; i++) {
                var method = trace.GetFrame(i)?.GetMethod();
                var type = method?.DeclaringType;
                if (type == null) continue;
                string ns = type.Namespace ?? "";
                if (!ns.StartsWith("Kingmaker") && !ns.StartsWith("WrathTactics") && !ns.StartsWith("TurnBased")) continue;
                if (type == typeof(InterruptPatch)) continue;
                string frame = type.Name + "." + method.Name;
                // Collapse overload chains (InterruptAndRemoveCommand x3, Run x2) so the
                // budget reaches the actual issuer — ClickGroundHandler, AiBrainController,
                // CommandExecutor — instead of stopping inside UnitCommands.
                if (frame == previous) continue;
                previous = frame;
                if (sb.Length > 0) sb.Append(" <- ");
                sb.Append(frame);
                taken++;
            }
            return sb.Length > 0 ? sb.ToString() : "(no engine frames)";
        }

        [HarmonyPatch(typeof(UnitCommand), nameof(UnitCommand.Interrupt), new[] { typeof(bool) })]
        static class InterruptPatch {
            static void Prefix(UnitCommand __instance) {
                try {
                    if (!Relevant(__instance) || __instance.IsFinished) return;
                    string line = $"{Who(__instance)}: {Describe(__instance)} interrupted (started={__instance.IsStarted}, acted={__instance.IsActed}{State(__instance)}) by {CallerSummary(6)}";
                    if (PlayerCommandGuard.IsOurs(__instance.Executor, __instance)) Log.Engine.Info(line);
                    else Log.Engine.Debug(line);
                } catch (Exception ex) {
                    // Per-frame guard: diagnostics must never break the engine's interrupt path.
                    Log.Engine.Debug($"CommandDiagnostics.Interrupt failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }
    }
}
