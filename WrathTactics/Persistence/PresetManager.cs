using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using WrathTactics.Models;

namespace WrathTactics.Persistence {
    public static class PresetManager {
        static string PresetDir => Path.Combine(Main.ModPath, "Presets");

        public static List<string> GetPresetNames() {
            if (!Directory.Exists(PresetDir)) return new List<string>();
            return Directory.GetFiles(PresetDir, "*.json")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n)
                .ToList();
        }

        public static List<TacticsRule> LoadPreset(string name) {
            var path = Path.Combine(PresetDir, $"{name}.json");
            if (!File.Exists(path)) {
                Main.Error($"[Presets] Preset not found: {name}");
                return new List<TacticsRule>();
            }

            try {
                var json = File.ReadAllText(path);
                var rules = JsonConvert.DeserializeObject<List<TacticsRule>>(json);
                Main.Log($"[Presets] Loaded preset '{name}' ({rules.Count} rules)");
                return rules;
            } catch (Exception ex) {
                Main.Error(ex, $"[Presets] Failed to load preset '{name}'");
                return new List<TacticsRule>();
            }
        }

        static string SanitizeName(string name) {
            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray());
            return string.IsNullOrWhiteSpace(sanitized) ? "preset" : sanitized.Trim();
        }

        public static void SavePreset(string name, List<TacticsRule> rules) {
            name = SanitizeName(name);
            try {
                Directory.CreateDirectory(PresetDir);
                var path = Path.Combine(PresetDir, $"{name}.json");
                var json = JsonConvert.SerializeObject(rules, Formatting.Indented);
                File.WriteAllText(path, json);
                Main.Log($"[Presets] Saved preset '{name}' ({rules.Count} rules)");
            } catch (Exception ex) {
                Main.Error(ex, $"[Presets] Failed to save preset '{name}'");
            }
        }

        public static void DeletePreset(string name) {
            name = SanitizeName(name);
            var path = Path.Combine(PresetDir, $"{name}.json");
            if (File.Exists(path)) {
                File.Delete(path);
                Main.Log($"[Presets] Deleted preset '{name}'");
            }
        }
    }
}
