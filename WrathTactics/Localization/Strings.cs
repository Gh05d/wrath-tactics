using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Kingmaker.Localization;
using Kingmaker.Localization.Shared;
using Newtonsoft.Json;

namespace WrathTactics.Localization {
    public static class Strings {
        static readonly Dictionary<Locale, Dictionary<string, string>> Packs = new();
        static bool initialised;

        public static void Initialise() {
            if (initialised) return;
            initialised = true;
            AddLanguage(Locale.enGB, "en_GB.json");
            TryAddLanguage(Locale.deDE, "de_DE.json");
            TryAddLanguage(Locale.frFR, "fr_FR.json");
            TryAddLanguage(Locale.zhCN, "zh_CN.json");
            TryAddLanguage(Locale.ruRU, "ru_RU.json");
        }

        public static string Get(string key, Locale locale) {
            if (string.IsNullOrEmpty(key)) return key;
            if (Packs.TryGetValue(locale, out var pack) && pack.TryGetValue(key, out var value))
                return value;
            if (locale != Locale.enGB && Packs.TryGetValue(Locale.enGB, out var fallback) && fallback.TryGetValue(key, out var fb))
                return fb;
            return key;
        }

        // Defense-in-depth: LocalizationManager.CurrentLocale dereferences
        // SettingsRoot.Game.Main.Localization, which is null until the game's settings
        // have loaded. Any i18n() call from a UMM-load-time path (Main.Load, Harmony
        // patches firing during game init) would NRE. Fall back to enGB if the getter
        // is not yet safe — UI-time and combat-time callers see the real locale.
        public static Locale Current {
            get {
                try { return LocalizationManager.CurrentLocale; }
                catch { return Locale.enGB; }
            }
        }

        public static string i18n(this string key) => Get(key, Current);

        public static string Format(string key, params object[] args) {
            var template = Get(key, Current);
            if (args == null || args.Length == 0) return template;
            try { return string.Format(template, args); } catch { return template; }
        }

        static void AddLanguage(Locale locale, string fileName) {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = $"WrathTactics.Localization.{fileName}";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) {
                Logging.Log.Engine.Warn($"Localization resource missing: {resourceName}");
                return;
            }
            using var reader = new StreamReader(stream);
            using var jsonReader = new JsonTextReader(reader);
            var serializer = new JsonSerializer();
            Packs[locale] = serializer.Deserialize<Dictionary<string, string>>(jsonReader)
                ?? new Dictionary<string, string>();
            Logging.Log.Engine.Info($"Loaded localization pack: {locale} ({Packs[locale].Count} entries)");
        }

        static void TryAddLanguage(Locale locale, string fileName) {
            try { AddLanguage(locale, fileName); } catch (System.Exception ex) {
                Logging.Log.Engine.Warn($"Failed to load {fileName}: {ex.Message}");
            }
        }
    }
}
