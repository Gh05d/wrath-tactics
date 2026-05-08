using System.Collections.Generic;
using System.Linq;
using Kingmaker;
using Kingmaker.EntitySystem.Entities;

namespace WrathTactics.Engine {
    /// <summary>
    /// Live list of party-and-pet units for picker UIs (Subject=AllyByName,
    /// TargetType.SpecificAlly, IsTargetingAlly/IsTargetedByAlly Value2 filter).
    ///
    /// Identifier is <see cref="UnitEntityData.UniqueId"/> — stable within a save,
    /// not portable across saves. Per-save tactics-{GameId}.json scopes match this.
    /// Presets that store an AllyByName condition or SpecificAlly target will not
    /// resolve when imported into a different save (Resolve returns null → condition
    /// fails / target unresolvable). Acceptable: user re-picks ally on import.
    /// </summary>
    public static class AllyProvider {
        public struct AllyEntry {
            public string UniqueId;
            public string DisplayName;
        }

        public static IReadOnlyList<AllyEntry> GetAll() {
            var player = Game.Instance?.Player;
            if (player?.PartyAndPets == null) return System.Array.Empty<AllyEntry>();
            return player.PartyAndPets
                .Where(u => u != null && u.IsInGame)
                .Select(u => new AllyEntry {
                    UniqueId = u.UniqueId,
                    DisplayName = string.IsNullOrEmpty(u.CharacterName) ? u.UniqueId : u.CharacterName,
                })
                .ToList();
        }

        public static UnitEntityData Resolve(string uniqueId) {
            if (string.IsNullOrEmpty(uniqueId)) return null;
            var player = Game.Instance?.Player;
            if (player?.PartyAndPets == null) return null;
            return player.PartyAndPets.FirstOrDefault(u => u != null && u.UniqueId == uniqueId);
        }

        public static string GetLabel(string uniqueId) {
            if (string.IsNullOrEmpty(uniqueId)) return "";
            var resolved = Resolve(uniqueId);
            if (resolved != null && !string.IsNullOrEmpty(resolved.CharacterName))
                return resolved.CharacterName;
            return uniqueId;
        }
    }
}
