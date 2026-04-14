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

        // Per-rule cooldown tracking: (unitId, ruleId) -> last fire game time
        static readonly Dictionary<(string, string), float> cooldowns = new Dictionary<(string, string), float>();

        public static void Tick(float gameTimeSec) {
            if (!Game.Instance.Player.IsInCombat) {
                if (wasInCombat) {
                    wasInCombat = false;
                    cooldowns.Clear();
                    Log.Engine.Info("Combat ended, cooldowns cleared");
                }
                return;
            }

            if (!wasInCombat) {
                wasInCombat = true;
                combatStartTime = gameTimeSec;
                Log.Engine.Info("Combat started");
                // Log party composition once per combat for diagnostics
                var partyNames = new List<string>();
                foreach (var u in Game.Instance.Player.Party) {
                    partyNames.Add($"{u.CharacterName}({u.UniqueId}) inGame={u.IsInGame}");
                }
                Log.Engine.Info($"Combat party: {string.Join(", ", partyNames)}");
            }

            var config = ConfigManager.Current;
            float interval = config.TickIntervalSeconds;
            if (gameTimeSec - lastTickTime < interval) return;
            lastTickTime = gameTimeSec;

            // Skip if BubbleBuffs is currently executing
            if (BubbleBuffsCompat.IsExecuting()) return;

            foreach (var unit in Game.Instance.Player.Party) {
                if (!unit.IsInGame || unit.HPLeft <= 0) continue;
                if (!config.IsEnabled(unit.UniqueId)) continue;
                EvaluateUnit(unit, config, gameTimeSec);
            }
        }

        static void EvaluateUnit(UnitEntityData unit, TacticsConfig config, float gameTimeSec) {
            // No command check — let the game's command system handle conflicts.
            // Our cooldown system (1 round = 6s) prevents spam.
            // BubbleBuffs also queues commands without checking.

            // Evaluate global rules first, then character-specific
            var globalRules = config.GlobalRules;
            var charRules = config.GetRulesForCharacter(unit.UniqueId);

            if (TryExecuteRules(globalRules, unit, "global", gameTimeSec))
                return;
            TryExecuteRules(charRules, unit, unit.CharacterName, gameTimeSec);
        }

        static bool TryExecuteRules(List<TacticsRule> rules, UnitEntityData unit,
            string source, float gameTimeSec) {
            for (int i = 0; i < rules.Count; i++) {
                var rule = rules[i];
                if (!rule.Enabled) continue;

                // Check cooldown
                var cooldownKey = (unit.UniqueId, rule.Id);
                float cooldownSec = rule.CooldownRounds * 6f;
                if (cooldowns.TryGetValue(cooldownKey, out float lastFired)) {
                    if (gameTimeSec - lastFired < cooldownSec) {
                        Log.Engine.Trace($"{unit.CharacterName} Rule {i} \"{rule.Name}\": on cooldown ({gameTimeSec - lastFired:F1}s / {cooldownSec:F0}s)");
                        continue;
                    }
                }

                // Clear matched entities before evaluating conditions
                ConditionEvaluator.ClearMatchedEntities();

                // Evaluate conditions
                bool match = ConditionEvaluator.Evaluate(rule, unit);
                if (!match) {
                    Log.Engine.Trace($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): conditions not met");
                    continue;
                }

                // Resolve target
                var target = TargetResolver.Resolve(rule.Target, unit);

                // Validate action
                if (!ActionValidator.CanExecute(rule.Action, unit, target)) {
                    Log.Engine.Warn($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): MATCH but action not executable");
                    continue;
                }

                // Execute!
                if (CommandExecutor.Execute(rule.Action, unit, target)) {
                    cooldowns[cooldownKey] = gameTimeSec;
                    Log.Engine.Info($"{unit.CharacterName} Rule {i} \"{rule.Name}\" ({source}): EXECUTED -> {target?.CharacterName ?? "self"}");
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
            cooldowns.Clear();
        }
    }
}
