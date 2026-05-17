using System.Collections.Generic;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.UnitLogic.Commands.Base;
using WrathTactics.Models;

namespace WrathTactics.Engine {
    /// <summary>
    /// Per-unit memory of the last rule that issued a UnitCommand. Used by
    /// TacticsEvaluator to gate evaluation while a mod-issued command is still
    /// in flight: only rules strictly above the active rule (in global-then-
    /// character priority order) may preempt; lower-priority rules wait until
    /// the current command finishes. This is true DAO-style tactics behaviour
    /// (higher-prio interrupt = ok, lower-prio interrupt = forbidden).
    ///
    /// PlayerCommandGuard remains responsible for the orthogonal foreign-cast
    /// gate (player- or other-mod-issued casts in the Standard slot).
    /// </summary>
    public static class ActiveRuleTracker {
        public struct Entry {
            public RuleListSource Source;
            public string EntryId;
            public UnitCommand Command;
        }

        public struct Resolution {
            /// <summary>True iff the active EntryId no longer exists in its list
            /// (e.g. user deleted the rule mid-combat). Caller should Clear().</summary>
            public bool Stale;
            /// <summary>Exclusive upper bound for the globals iteration.
            /// int.MaxValue = no gate; 0 = skip all globals.</summary>
            public int GlobalGate;
            /// <summary>Exclusive upper bound for the character-rules iteration.</summary>
            public int CharGate;
        }

        static readonly Dictionary<string, Entry> activeByUnit = new Dictionary<string, Entry>();

        public static void Record(UnitEntityData unit, RuleListSource source, string entryId, UnitCommand cmd) {
            if (unit == null || cmd == null || string.IsNullOrEmpty(entryId)) return;
            activeByUnit[unit.UniqueId] = new Entry {
                Source = source,
                EntryId = entryId,
                Command = cmd,
            };
        }

        /// <summary>
        /// Returns the active entry iff the tracked command is still in flight
        /// (not null and not IsFinished). Auto-clears finished/null commands.
        /// </summary>
        public static Entry? GetActive(UnitEntityData unit) {
            if (unit == null) return null;
            if (!activeByUnit.TryGetValue(unit.UniqueId, out var entry)) return null;
            if (entry.Command == null || entry.Command.IsFinished) {
                activeByUnit.Remove(unit.UniqueId);
                return null;
            }
            return entry;
        }

        public static void Clear(UnitEntityData unit) {
            if (unit == null) return;
            activeByUnit.Remove(unit.UniqueId);
        }

        public static void Reset() {
            activeByUnit.Clear();
        }

        /// <summary>
        /// Pure helper: given the active tracker entry and the two rule lists,
        /// return per-list priority gates. globalRules has priority over charRules.
        /// </summary>
        internal static Resolution Resolve(
            RuleListSource activeSource,
            string activeEntryId,
            IReadOnlyList<TacticsRule> globalRules,
            IReadOnlyList<TacticsRule> charRules)
        {
            var list = activeSource == RuleListSource.Global ? globalRules : charRules;
            int idx = -1;
            for (int i = 0; i < list.Count; i++) {
                if (list[i].Id == activeEntryId) { idx = i; break; }
            }
            if (idx < 0) {
                return new Resolution {
                    Stale = true,
                    GlobalGate = int.MaxValue,
                    CharGate = int.MaxValue,
                };
            }
            if (activeSource == RuleListSource.Global) {
                return new Resolution { GlobalGate = idx, CharGate = 0 };
            }
            return new Resolution { GlobalGate = int.MaxValue, CharGate = idx };
        }
    }
}
