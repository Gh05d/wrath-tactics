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
        /// <summary>Optional link to a preset; when set, rule body (conditions/action/target) is resolved from the preset at runtime.</summary>
        [JsonProperty] public string PresetId { get; set; }
    }

    public class ConditionGroup {
        [JsonProperty] public List<Condition> Conditions { get; set; } = new();
    }

    public class Condition {
        [JsonProperty] public ConditionSubject Subject { get; set; }
        [JsonProperty] public ConditionProperty Property { get; set; }
        [JsonProperty] public ConditionOperator Operator { get; set; }
        // Operator applied to the count itself for AllyCount/EnemyCount (e.g. count < 3, count >= 2).
        // Defaults to GreaterOrEqual so legacy saves without this field keep their original behavior.
        [JsonProperty] public ConditionOperator CountOperator { get; set; } = ConditionOperator.GreaterOrEqual;
        [JsonProperty] public string Value { get; set; } = "";
        [JsonProperty] public string Value2 { get; set; } = "";  // For AllyCount/EnemyCount: the count threshold
    }

    public class ActionDef {
        [JsonProperty] public ActionType Type { get; set; }
        [JsonProperty] public string AbilityId { get; set; } = "";
        // CastSpell fallback chain: tried in order after AbilityId when the primary resolver misses
        // (no slot, no scroll, UMD fail, etc.). Each entry goes through the full Sources mask,
        // so a fallback can still fall through Spellbook -> Wand -> Scroll -> Potion for itself.
        // Empty on legacy rules; only consulted by ActionType.CastSpell.
        [JsonProperty] public List<string> FallbackAbilityIds { get; set; } = new();
        [JsonProperty] public HealMode HealMode { get; set; } = HealMode.Any;
        [JsonProperty] public HealSourceMask HealSources { get; set; } = HealSourceMask.All;
        [JsonProperty] public SpellSourceMask Sources { get; set; } = SpellSourceMask.All;
        [JsonProperty] public ThrowSplashMode SplashMode { get; set; } = ThrowSplashMode.Any;
        [JsonProperty] public ToggleMode ToggleMode { get; set; } = ToggleMode.On;
    }

    public class TargetDef {
        [JsonProperty] public TargetType Type { get; set; }
        [JsonProperty] public string Filter { get; set; } = "";
    }
}
