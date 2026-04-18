using System.Collections.Generic;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>
    /// Seeded once per fresh install via PresetRegistry.SeedDefaults. Uses fixed IDs so
    /// "file exists?" check is idempotent across mod reloads and version bumps. User
    /// deletions and manual edits are never overwritten.
    /// </summary>
    public static class DefaultPresets {
        public static List<TacticsRule> Build() {
            return new List<TacticsRule> {
                EmergencySelfHeal(),
                PartyChannelHeal(),
                CounterSwarms(),
                CoupDeGrace(),
                ChannelVsUndead(),
                SmiteEvil(),
            };
        }

        static TacticsRule EmergencySelfHeal() => new TacticsRule {
            Id = "default-emergency-self-heal",
            Name = "Emergency Self-Heal",
            Enabled = true,
            Priority = 0,
            CooldownRounds = 1,
            ConditionGroups = new List<ConditionGroup> {
                new ConditionGroup { Conditions = new List<Condition> {
                    new Condition {
                        Subject = ConditionSubject.Self,
                        Property = ConditionProperty.HpPercent,
                        Operator = ConditionOperator.LessThan,
                        Value = "30",
                    },
                }},
            },
            Action = new ActionDef { Type = ActionType.Heal, HealMode = HealMode.Any },
            Target = new TargetDef { Type = TargetType.Self },
        };

        static TacticsRule PartyChannelHeal() => new TacticsRule {
            Id = "default-party-channel-heal",
            Name = "Party Heal (Channel Positive)",
            Enabled = true,
            Priority = 0,
            CooldownRounds = 1,
            ConditionGroups = new List<ConditionGroup> {
                new ConditionGroup { Conditions = new List<Condition> {
                    new Condition {
                        Subject = ConditionSubject.AllyCount,
                        Property = ConditionProperty.HpPercent,
                        Operator = ConditionOperator.LessThan,
                        Value = "60",
                        Value2 = "2",
                    },
                }},
            },
            Action = new ActionDef {
                Type = ActionType.CastAbility,
                AbilityId = "f5fc9a1a2a3c1a946a31b320d1dd31b2",  // Cleric ChannelEnergy (heal)
            },
            Target = new TargetDef { Type = TargetType.Self },
        };

        static TacticsRule CounterSwarms() => new TacticsRule {
            Id = "default-counter-swarms",
            Name = "Counter Swarms (Splash)",
            Enabled = true,
            Priority = 0,
            CooldownRounds = 1,
            ConditionGroups = new List<ConditionGroup> {
                new ConditionGroup { Conditions = new List<Condition> {
                    new Condition {
                        Subject = ConditionSubject.Enemy,
                        Property = ConditionProperty.CreatureType,
                        Operator = ConditionOperator.Equal,
                        Value = "Swarm",
                    },
                }},
            },
            Action = new ActionDef { Type = ActionType.ThrowSplash, SplashMode = ThrowSplashMode.Strongest },
            Target = new TargetDef { Type = TargetType.EnemyNearest },
        };

        static TacticsRule CoupDeGrace() => new TacticsRule {
            Id = "default-coup-de-grace",
            Name = "Coup de Grace on Helpless",
            Enabled = true,
            Priority = 0,
            CooldownRounds = 1,
            ConditionGroups = new List<ConditionGroup> {
                new ConditionGroup { Conditions = new List<Condition> {
                    new Condition {
                        Subject = ConditionSubject.Enemy,
                        Property = ConditionProperty.HasCondition,
                        Operator = ConditionOperator.Equal,
                        Value = "Sleeping",
                    },
                }},
                new ConditionGroup { Conditions = new List<Condition> {
                    new Condition {
                        Subject = ConditionSubject.Enemy,
                        Property = ConditionProperty.HasCondition,
                        Operator = ConditionOperator.Equal,
                        Value = "Paralyzed",
                    },
                }},
            },
            Action = new ActionDef { Type = ActionType.AttackTarget },
            Target = new TargetDef { Type = TargetType.ConditionTarget },
        };

        static TacticsRule ChannelVsUndead() => new TacticsRule {
            Id = "default-channel-vs-undead",
            Name = "Channel Against Undead",
            Enabled = true,
            Priority = 0,
            CooldownRounds = 1,
            ConditionGroups = new List<ConditionGroup> {
                new ConditionGroup { Conditions = new List<Condition> {
                    new Condition {
                        Subject = ConditionSubject.Enemy,
                        Property = ConditionProperty.CreatureType,
                        Operator = ConditionOperator.Equal,
                        Value = "Undead",
                    },
                }},
            },
            Action = new ActionDef {
                Type = ActionType.CastAbility,
                AbilityId = "279447a6bf2d3544d93a0a39c3b8e91d",  // Cleric ChannelPositiveHarm
            },
            Target = new TargetDef { Type = TargetType.Self },
        };

        static TacticsRule SmiteEvil() => new TacticsRule {
            Id = "default-smite-evil",
            Name = "Smite Evil",
            Enabled = true,
            Priority = 0,
            CooldownRounds = 1,
            ConditionGroups = new List<ConditionGroup> {
                new ConditionGroup { Conditions = new List<Condition> {
                    new Condition {
                        Subject = ConditionSubject.Enemy,
                        Property = ConditionProperty.Alignment,
                        Operator = ConditionOperator.Equal,
                        Value = "Evil",
                    },
                }},
            },
            Action = new ActionDef {
                Type = ActionType.CastAbility,
                AbilityId = "7bb9eb2042e67bf489ccd1374423cdec",  // Paladin SmiteEvilAbility
            },
            Target = new TargetDef { Type = TargetType.EnemyHighestThreat },
        };
    }
}
