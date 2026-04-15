using Newtonsoft.Json;
using System.Collections.Generic;

namespace WrathTactics.Models {
    public class TacticsRule {
        [JsonProperty] public string Id { get; set; } = System.Guid.NewGuid().ToString();
        [JsonProperty] public string Name { get; set; } = "New Rule";
        [JsonProperty] public bool Enabled { get; set; } = true;
        [JsonProperty] public int Priority { get; set; }
        [JsonProperty] public int CooldownRounds { get; set; } = 1;
        [JsonProperty] public List<ConditionGroup> ConditionGroups { get; set; } = new();
        [JsonProperty] public ActionDef Action { get; set; } = new();
        [JsonProperty] public TargetDef Target { get; set; } = new();
        /// <summary>
        /// If set, this rule is linked to a preset. Conditions/Action/Target are resolved live
        /// from the preset at eval/render time. Any edit materializes current preset data into
        /// this rule and clears PresetId (link break).
        /// </summary>
        [JsonProperty] public string PresetId { get; set; } = null;
    }

    public class ConditionGroup {
        [JsonProperty] public List<Condition> Conditions { get; set; } = new();
    }

    public class Condition {
        [JsonProperty] public ConditionSubject Subject { get; set; }
        [JsonProperty] public ConditionProperty Property { get; set; }
        [JsonProperty] public ConditionOperator Operator { get; set; }
        [JsonProperty] public string Value { get; set; } = "";
        [JsonProperty] public string Value2 { get; set; } = "";  // For AllyCount/EnemyCount: the count threshold
    }

    public class ActionDef {
        [JsonProperty] public ActionType Type { get; set; }
        [JsonProperty] public string AbilityId { get; set; } = "";
        [JsonProperty] public HealMode HealMode { get; set; } = HealMode.Any;
        [JsonProperty] public ThrowSplashMode SplashMode { get; set; } = ThrowSplashMode.Any;
    }

    public class TargetDef {
        [JsonProperty] public TargetType Type { get; set; }
        [JsonProperty] public string Filter { get; set; } = "";
    }
}
