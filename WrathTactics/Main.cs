using System;
using HarmonyLib;
using Kingmaker;
using Kingmaker.PubSubSystem;
using UnityModManagerNet;
using WrathTactics.Logging;

namespace WrathTactics {
    static class Main {
        static Harmony harmony;
        public static UnityModManager.ModEntry ModEntry;
        public static string ModPath;
        static SaveLoadWatcher saveLoadWatcher;

        static bool Load(UnityModManager.ModEntry modEntry) {
            ModEntry = modEntry;
            ModPath = modEntry.Path;
            modEntry.OnUnload = OnUnload;
            modEntry.OnUpdate = OnUpdate;

            Logging.DebugLog.Init(modEntry.Path);
            Logging.Log.Engine.Info($"Wrath Tactics loading (session log: {Logging.DebugLog.CurrentSessionPath})");

            harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll();

            UI.TacticsPanel.Install();

            EventBus.Subscribe(saveLoadWatcher = new SaveLoadWatcher());

            Logging.Log.Engine.Info("Wrath Tactics loaded.");
            modEntry.Logger.Log("Wrath Tactics loaded.");
            return true;
        }

        static void OnUpdate(UnityModManager.ModEntry modEntry, float delta) {
            try {
                if (Game.Instance?.Player == null) return;
                float gameTime = (float)Game.Instance.Player.GameTime.TotalSeconds;
                Engine.TacticsEvaluator.Tick(gameTime);
            } catch (Exception ex) {
                Logging.Log.Engine.Error(ex, "Tick error");
            }
        }

        static bool OnUnload(UnityModManager.ModEntry modEntry) {
            try {
                if (saveLoadWatcher != null) EventBus.Unsubscribe(saveLoadWatcher);
                UI.TacticsPanel.Uninstall();
                harmony.UnpatchAll(modEntry.Info.Id);
            } finally {
                Logging.DebugLog.Shutdown();
            }
            return true;
        }

        // === Thin-wrapper API for legacy callers ===
        // All existing Main.Log/DebugLog/Debug/Error calls delegate to Log.Engine
        // until they get migrated to the proper category in later tasks.

        public static void Log(string msg) {
            Logging.Log.Engine.Info(msg);
            ModEntry?.Logger?.Log(msg);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        public static void Debug(string msg) => Logging.Log.Engine.Debug(msg);

        public static void DebugLog(string msg) {
            if (Persistence.ConfigManager.Current.DebugLogging)
                Logging.Log.Engine.Debug(msg);
        }

        public static void Error(string msg) {
            Logging.Log.Engine.Error(msg);
        }

        public static void Error(Exception ex, string context = null) {
            Logging.Log.Engine.Error(ex, context);
        }

        class SaveLoadWatcher : IAreaHandler {
            public void OnAreaDidLoad() {
                Persistence.ConfigManager.Reset();
                Engine.TacticsEvaluator.Reset();
                Logging.Log.Game.Info("Area loaded — config and evaluator state reset");
            }
            public void OnAreaBeginUnloading() { }
        }
    }
}
