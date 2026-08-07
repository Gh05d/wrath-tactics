using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Persistence {
    /// <summary>
    /// Disk layer for rule packs — one JSON file per pack, write-then-rename like
    /// PresetManager. Packs live in their own directory ({ModPath}/Packs) so a pack file
    /// can never be picked up by PresetManager.LoadAll, which globs Presets/*.json.
    /// The *From variants take an explicit directory and carry no Game API dependency,
    /// which is what the unit tests drive.
    /// </summary>
    public static class PackManager {
        internal static string PackDir => Path.Combine(Main.ModPath ?? ".", "Packs");

        public static List<TacticsPack> LoadAll() => LoadAllFrom(PackDir);
        public static bool Save(TacticsPack pack) => SaveTo(PackDir, pack);
        public static bool Delete(string packId) => DeleteFrom(PackDir, packId);

        // --- Testable core (no Game API; pure file + JSON) ---

        internal static List<TacticsPack> LoadAllFrom(string dir) {
            var result = new List<TacticsPack>();
            if (!Directory.Exists(dir)) return result;

            foreach (var path in Directory.GetFiles(dir, "*.json")) {
                try {
                    var pack = JsonConvert.DeserializeObject<TacticsPack>(File.ReadAllText(path));
                    if (pack == null) continue;
                    if (string.IsNullOrEmpty(pack.Id)) pack.Id = Guid.NewGuid().ToString();
                    if (pack.PresetIds == null) pack.PresetIds = new List<string>();
                    result.Add(pack);
                } catch (JsonException ex) {
                    Log.Persistence.Warn($"Skipping unreadable pack file {path}: {ex.Message}");
                } catch (IOException ex) {
                    Log.Persistence.Warn($"Skipping unreadable pack file {path}: {ex.Message}");
                }
            }
            return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Write-then-rename so a crash mid-write cannot leave a partial file.
        /// Returns false on any failure (already logged) — UI callers must surface it,
        /// otherwise the user sees a phantom-saved pack that vanishes on reload.
        /// </summary>
        internal static bool SaveTo(string dir, TacticsPack pack) {
            if (pack == null || string.IsNullOrEmpty(pack.Id)) {
                Log.Persistence.Warn("PackManager.Save called with null pack or empty Id — ignored");
                return false;
            }
            try {
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"{pack.Id}.json");
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(pack, Formatting.Indented));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                Log.Persistence.Info($"Saved pack '{pack.Name}' (id={pack.Id}, {pack.PresetIds.Count} member(s))");
                return true;
            } catch (Exception ex) {
                Log.Persistence.Error(ex, $"Failed to save pack '{pack.Name}'");
                return false;
            }
        }

        /// <summary>Returns true if the file was removed or never existed.</summary>
        internal static bool DeleteFrom(string dir, string packId) {
            if (string.IsNullOrEmpty(packId)) return false;
            try {
                var path = Path.Combine(dir, $"{packId}.json");
                if (File.Exists(path)) {
                    File.Delete(path);
                    Log.Persistence.Info($"Deleted pack id={packId}");
                }
                return true;
            } catch (Exception ex) {
                Log.Persistence.Error(ex, $"Failed to delete pack id={packId}");
                return false;
            }
        }
    }
}
