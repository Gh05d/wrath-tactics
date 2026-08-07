using Newtonsoft.Json;
using System.Collections.Generic;

namespace WrathTactics.Models {
    /// <summary>
    /// A named, colour-coded bundle of preset IDs. Applying a pack appends one
    /// PresetId-linked rule per member (see PackRegistry.PlanApply), so packs hold links,
    /// not rule bodies — editing a member preset propagates to every character the pack
    /// was applied to. Persisted one file per pack under {ModPath}/Packs/.
    /// </summary>
    public class TacticsPack {
        [JsonProperty] public string Id { get; set; } = System.Guid.NewGuid().ToString();
        [JsonProperty] public string Name { get; set; } = "New Pack";
        /// <summary>Index into UI.PackPalette.Colors. Out-of-range values resolve to index 0.</summary>
        [JsonProperty] public int ColorIndex { get; set; }
        /// <summary>
        /// Preset IDs in application order. May contain IDs whose preset no longer exists
        /// (a preset deleted while the mod was not running) — every consumer resolves defensively.
        /// </summary>
        [JsonProperty] public List<string> PresetIds { get; set; } = new List<string>();
    }
}
