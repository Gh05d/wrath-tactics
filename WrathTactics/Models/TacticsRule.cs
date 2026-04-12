using Newtonsoft.Json;
using System.Collections.Generic;

namespace WrathTactics.Models {
    public class TacticsRule {
        [JsonProperty] public string Name { get; set; } = "New Rule";
        [JsonProperty] public bool Enabled { get; set; } = true;
        [JsonProperty] public int Priority { get; set; }
        [JsonProperty] public int CooldownRounds { get; set; } = 1;
        [JsonProperty] public List<ConditionGroup> ConditionGroups { get; set; } = new();
        [JsonProperty] public ActionDef Action { get; set; } = new();
        [JsonProperty] public TargetDef Target { get; set; } = new();
    }

    public class ConditionGroup {
        [JsonProperty] public List<Condition> Conditions { get; set; } = new();
    }

    public class Condition {
        [JsonProperty] public ConditionSubject Subject { get; set; }
        [JsonProperty] public ConditionProperty Property { get; set; }
        [JsonProperty] public ConditionOperator Operator { get; set; }
        [JsonProperty] public string Value { get; set; } = "";
    }

    public class ActionDef {
        [JsonProperty] public ActionType Type { get; set; }
        [JsonProperty] public string AbilityId { get; set; } = "";
    }

    public class TargetDef {
        [JsonProperty] public TargetType Type { get; set; }
        [JsonProperty] public string Filter { get; set; } = "";
    }
}
