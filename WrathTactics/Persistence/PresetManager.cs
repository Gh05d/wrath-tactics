using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WrathTactics.Logging;
using WrathTactics.Models;

namespace WrathTactics.Persistence {
    public static class PresetManager {
        static string PresetDir => Path.Combine(Main.ModPath, "Presets");

        public static List<TacticsRule> LoadAll() {
            var result = new List<TacticsRule>();
            if (!Directory.Exists(PresetDir)) return result;

            foreach (var path in Directory.GetFiles(PresetDir, "*.json")) {
                var fileName = Path.GetFileNameWithoutExtension(path);
                try {
                    var json = File.ReadAllText(path);
                    var token = JToken.Parse(json);

                    if (token.Type == JTokenType.Array) {
                        Log.Persistence.Warn($"Preset '{fileName}' is in legacy list format — skipping. Re-create under the new single-rule model.");
                        continue;
                    }

                    var rule = token.ToObject<TacticsRule>();
                    if (rule == null) continue;

                    if (string.IsNullOrEmpty(rule.Id))
                        rule.Id = Guid.NewGuid().ToString();
                    result.Add(rule);
                } catch (Exception ex) {
                    Log.Persistence.Error(ex, $"Failed to load preset file {path}");
                }
            }
            return result.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Writes the preset JSON via write-then-rename atomic pattern.
        /// Returns true on successful disk write, false on any exception (already logged).
        /// UI callers must surface a failure — otherwise users see a phantom-saved preset
        /// that disappears on next mod reload.
        /// </summary>
        public static bool Save(TacticsRule preset) {
            if (preset == null || string.IsNullOrEmpty(preset.Id)) {
                Log.Persistence.Warn("Save called with null preset or empty Id — ignored");
                return false;
            }
            try {
                Directory.CreateDirectory(PresetDir);
                var path = Path.Combine(PresetDir, $"{preset.Id}.json");
                var tmp = path + ".tmp";
                var json = JsonConvert.SerializeObject(preset, Formatting.Indented);
                // Write-then-rename so a crash mid-write can't leave an empty/partial file.
                File.WriteAllText(tmp, json);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
                Log.Persistence.Info($"Saved preset '{preset.Name}' (id={preset.Id})");
                return true;
            } catch (Exception ex) {
                Log.Persistence.Error(ex, $"Failed to save preset '{preset.Name}'");
                return false;
            }
        }

        /// <summary>
        /// Returns true if the file was removed (or never existed), false if deletion threw.
        /// </summary>
        public static bool Delete(string presetId) {
            if (string.IsNullOrEmpty(presetId)) return false;
            try {
                var path = Path.Combine(PresetDir, $"{presetId}.json");
                if (File.Exists(path)) {
                    File.Delete(path);
                    Log.Persistence.Info($"Deleted preset id={presetId}");
                }
                return true;
            } catch (Exception ex) {
                Log.Persistence.Error(ex, $"Failed to delete preset id={presetId}");
                return false;
            }
        }
    }
}
