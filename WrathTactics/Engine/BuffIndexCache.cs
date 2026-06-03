using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using WrathTactics.Logging;

namespace WrathTactics.Engine {
    /// <summary>
    /// On-disk cache of the full buff blueprint index (GUID + display name + internal
    /// name). Lets the expensive one-time pack scan (<see cref="BuffPackScanner"/>) run
    /// only once per game-version / locale instead of every session.
    ///
    /// The picker needs metadata only — never the loaded blueprint objects — so a few KB
    /// of JSON replaces force-loading ~75k blueprints on every launch. Runtime HasBuff
    /// matching reads the buff off the unit (already loaded), not this index.
    ///
    /// Stamped with game version + locale: a game patch can add/rename buffs, and the
    /// display name is localized, so a stamp mismatch forces a fresh scan.
    /// </summary>
    public static class BuffIndexCache {
        [Serializable]
        public class IndexFile {
            public string GameVersion;
            public string Locale;
            public List<Record> Buffs;
        }

        [Serializable]
        public class Record {
            public string Guid;
            public string Name;
            public string Internal;
        }

        static string DefaultPath =>
            Path.Combine(Main.ModPath ?? ".", "Cache", "buff-index.json");

        static string CurrentGameVersion() {
            try { return Kingmaker.GameVersion.GetVersion() ?? "unknown"; }
            catch (Exception) { return "unknown"; }
        }

        static string CurrentLocale() {
            try { return Kingmaker.Localization.LocalizationManager.CurrentLocale.ToString(); }
            catch (Exception) { return "unknown"; }
        }

        public static bool TryLoad(out List<BuffBlueprintProvider.BuffEntry> buffs) {
            return TryLoadFrom(DefaultPath, CurrentGameVersion(), CurrentLocale(), out buffs);
        }

        public static bool Save(List<BuffBlueprintProvider.BuffEntry> buffs) {
            return SaveTo(DefaultPath, CurrentGameVersion(), CurrentLocale(), buffs);
        }

        // --- Testable core (no Game API; pure file + JSON) ---

        internal static bool TryLoadFrom(string path, string version, string locale,
            out List<BuffBlueprintProvider.BuffEntry> buffs) {
            buffs = null;
            try {
                if (!File.Exists(path)) return false;
                var file = JsonConvert.DeserializeObject<IndexFile>(File.ReadAllText(path));
                if (file?.Buffs == null) return false;
                if (!string.Equals(file.GameVersion, version, StringComparison.Ordinal)) return false;
                if (!string.Equals(file.Locale, locale, StringComparison.Ordinal)) return false;

                var result = new List<BuffBlueprintProvider.BuffEntry>(file.Buffs.Count);
                foreach (var r in file.Buffs) {
                    result.Add(new BuffBlueprintProvider.BuffEntry {
                        Guid = r.Guid, Name = r.Name, InternalName = r.Internal
                    });
                }
                buffs = result;
                return true;
            } catch (Exception ex) {
                Log.Engine.Warn($"BuffIndexCache: failed to load \"{path}\": {ex.Message}");
                return false;
            }
        }

        internal static bool SaveTo(string path, string version, string locale,
            List<BuffBlueprintProvider.BuffEntry> buffs) {
            try {
                var file = new IndexFile {
                    GameVersion = version, Locale = locale, Buffs = new List<Record>(buffs.Count)
                };
                foreach (var b in buffs) {
                    file.Buffs.Add(new Record { Guid = b.Guid, Name = b.Name, Internal = b.InternalName });
                }
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonConvert.SerializeObject(file, Formatting.Indented));
                return true;
            } catch (Exception ex) {
                Log.Engine.Error(ex, $"BuffIndexCache: failed to save \"{path}\"");
                return false;
            }
        }
    }
}
