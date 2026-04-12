using Newtonsoft.Json;
using System.Collections.Generic;

namespace WrathTactics.Models {
    public class TacticsConfig {
        [JsonProperty] public List<TacticsRule> GlobalRules { get; set; } = new();
        [JsonProperty] public Dictionary<string, List<TacticsRule>> CharacterRules { get; set; } = new();
        [JsonProperty] public Dictionary<string, bool> TacticsEnabled { get; set; } = new();
        [JsonProperty] public float TickIntervalSeconds { get; set; } = 3f;
        [JsonProperty] public bool DebugLogging { get; set; }

        public List<TacticsRule> GetRulesForCharacter(string unitId) {
            if (CharacterRules.TryGetValue(unitId, out var rules))
                return rules;
            return new List<TacticsRule>();
        }

        public bool IsEnabled(string unitId) {
            if (TacticsEnabled.TryGetValue(unitId, out var enabled))
                return enabled;
            return true;
        }
    }
}
