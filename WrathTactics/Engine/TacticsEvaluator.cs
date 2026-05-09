using System;
using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;
using WrathTactics.Compatibility;
using WrathTactics.Logging;
using WrathTactics.Models;
using WrathTactics.Persistence;

namespace WrathTactics.Engine {
    public static class TacticsEvaluator {
        static float lastTickTime;
        static float combatStartTime;
        static bool wasInCombat;
        static int tickCounter;

        // Per-rule cooldown tracking: (unitId, ruleId) -> last fire game time
        static readonly Dictionary<(string, string), float> cooldowns = new Dictionary<(string, string), float>();

        public static void Tick(float gameTimeSec) {
            bool inCombat = Game.Instance.Player.IsInCombat;

            // Combat-end transition: log + reset the foreign-command tracker. The legacy
            // RunPostCombatCleanup single-pass is gone — the next regular tick handles
            // cleanup with cooldowns honored.
            if (!inCombat && wasInCombat) {
                wasInCombat = false;
                PlayerCommandGuard.Reset();
                Log.Engine.Info("Combat ended");
            }

            // Combat-start transition.
            if (inCombat && !wasInCombat) {
                wasInCombat = true;
                combatStartTime = gameTimeSec;
                PlayerCommandGuard.Reset();
                Log.Engine.Info("Combat started");
                var partyNames = new List<string>();
                foreach (var u in Game.Instance.Player.PartyAndPets) {
                    partyNames.Add($"{u.CharacterName}({u.UniqueId}) inGame={u.IsInGame}");
                }
                Log.Engine.Info($"Combat party: {string.Join(", ", partyNames)}");
            }

            var config = ConfigManager.Current;
            float interval = inCombat
                ? config.TickIntervalSeconds
                : config.OutOfCombatTickIntervalSeconds;
            if (gameTimeSec - lastTickTime < interval) return;
            lastTickTime = gameTimeSec;

            tickCounter++;
            int evaluableUnits = 0;
            foreach (var u in Game.Instance.Player.PartyAndPets) {
                if (u.IsInGame && u.HPLeft > 0) evaluableUnits++;
            }
            Log.Engine.Trace($"Tick #{tickCounter} gameTime={gameTimeSec:F1}s inCombat={inCombat} evaluable={evaluableUnits}");

            if (BubbleBuffsCompat.IsExecuting()) return;

            foreach (var unit in Game.Instance.Player.PartyAndPets) {
                if (!unit.IsInGame || unit.HPLeft <= 0) continue;
                if (!config.IsEnabled(unit.UniqueId)) continue;
                EvaluateUnit(unit, config, gameTimeSec, inCombat);
            }
        }

        // Returns true iff the rule has at least one Combat.IsInCombat==false condition
        // anywhere in its ConditionGroups. Used as the out-of-combat opt-in gate; the
        // condition's actual matching during evaluation is handled by the existing
        // bucket-AND-OR logic in ConditionEvaluator.Evaluate. Looseness is intentional —
        // presence of the condition is the user's expressed "out-of-combat-fähig" intent.
        static bool RuleEnabledOutOfCombat(TacticsRule rule) {
            if (rule.ConditionGroups == null) return false;
            foreach (var group in rule.ConditionGroups) {
                if (group?.Conditions == null) continue;
                foreach (var c in group.Conditions) {
                    if (c.Subject != ConditionSubject.Combat) continue;
                    if (c.Property != ConditionProperty.IsInCombat) continue;
                    if (c.Operator != ConditionOperator.Equal) continue;
                    var v = c.Value?.Trim().ToLowerInvariant();
                    if (v == "false" || v == "0" || v == "no" || v == "nein") return true;
                }
            }
            return false;
        }

        static void EvaluateUnit(UnitEntityData unit, TacticsConfig config, float gameTimeSec, bool inCombat) {
            // Skip if a player- (or other-mod-) issued command is currently running. Our own
            // tactics commands stay in the tracked set and don't block — self-interruption
            // when a higher-priority rule matches mid-cast is intentional (DAO semantics).
            if (PlayerCommandGuard.HasForeignActiveCommand(unit)) {
                Log.Engine.Trace($"  Skip {unit.CharacterName}: player/foreign command active");
                return;
            }

            Log.Engine.Trace($"  Evaluating {unit.CharacterName} (hp={unit.HPLeft}/{unit.Stats.HitPoints.ModifiedValue}, id={unit.UniqueId}, inCombat={inCombat})");

            var globalRules = config.GlobalRules;
            var charRules = config.GetRulesForCharacter(unit.UniqueId);

            if (TryExecuteRules(globalRules, unit, "global", gameTimeSec, inCombat))
                return;
            TryExecuteRules(charRules, unit, unit.CharacterName, gameTimeSec, inCombat);
        }

        static bool TryExecuteRules(List<TacticsRule> rules, UnitEntityData unit,
            string source, float gameTimeSec, bool inCombat) {
            for (int i = 0; i < rules.Count; i++) {
                var entry = rules[i];
                if (!entry.Enabled) continue;

                var rule = PresetRegistry.Resolve(entry);

                // Out-of-combat opt-in gate. Rules without a Combat.IsInCombat==false
                // condition keep their pre-1.7.0 behavior (in-combat-only).
                if (!inCombat && !RuleEnabledOutOfCombat(rule)) {
                    continue;
                }

                // Check cooldown — key on entry.Id so linked copies cooldown independently
                var cooldownKey = (unit.UniqueId, entry.Id);
                float cooldownSec = rule.CooldownRounds * 6f;
                if (cooldowns.TryGetValue(cooldownKey, out float lastFired)) {
                    if (gameTimeSec - lastFired < cooldownSec) {
                        Log.Engine.Trace($"{unit.CharacterName} Rule {i} \"{rule.Name}\": on cooldown ({gameTimeSec - lastFired:F1}s / {cooldownSec:F0}s)");
                        continue;
                    }
                }

                ConditionEvaluator.ClearMatchedEntities();

                bool match = ConditionEvaluator.Evaluate(rule, unit);
                if (!match) {
                    Log.Engine.Trace($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): conditions not met");
                    continue;
                }

                var target = TargetResolver.Resolve(rule.Target, unit);

                if (!ActionValidator.CanExecute(rule.Action, unit, target)) {
                    Log.Engine.Warn($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): MATCH but action not executable");
                    continue;
                }

                if (CommandExecutor.Execute(rule.Action, unit, target)) {
                    cooldowns[cooldownKey] = gameTimeSec;
                    Log.Engine.Info($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): EXECUTED -> {FormatTarget(target)}");
                    return true;
                }
            }
            return false;
        }

        public static float GetCombatRoundsElapsed(float gameTimeSec) {
            if (!wasInCombat) return 0;
            return (gameTimeSec - combatStartTime) / 6f;
        }

        public static void Reset() {
            lastTickTime = 0;
            combatStartTime = 0;
            wasInCombat = false;
            tickCounter = 0;
            cooldowns.Clear();
        }

        static string FormatTarget(ResolvedTarget target) {
            if (target.Unit != null) return target.Unit.CharacterName;
            if (target.Point.HasValue) {
                var p = target.Point.Value;
                return $"point({p.x:F1},{p.y:F1},{p.z:F1})";
            }
            return "self";
        }
    }
}
