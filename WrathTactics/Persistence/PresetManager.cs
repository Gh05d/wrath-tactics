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

        public static void Save(TacticsRule preset) {
            if (preset == null || string.IsNullOrEmpty(preset.Id)) {
                Log.Persistence.Warn("Save called with null preset or empty Id — ignored");
                return;
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
            } catch (Exception ex) {
                Log.Persistence.Error(ex, $"Failed to save preset '{preset.Name}'");
            }
        }

        public static void Delete(string presetId) {
            if (string.IsNullOrEmpty(presetId)) return;
            try {
                var path = Path.Combine(PresetDir, $"{presetId}.json");
                if (File.Exists(path)) {
                    File.Delete(path);
                    Log.Persistence.Info($"Deleted preset id={presetId}");
                }
            } catch (Exception ex) {
                Log.Persistence.Error(ex, $"Failed to delete preset id={presetId}");
            }
        }
    }
}
