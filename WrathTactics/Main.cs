using System;
using HarmonyLib;
using Kingmaker;
using UnityModManagerNet;

namespace WrathTactics {
    static class Main {
        static Harmony harmony;
        static UnityModManager.ModEntry.ModLogger logger;
        public static UnityModManager.ModEntry ModEntry;
        public static string ModPath;

        static bool Load(UnityModManager.ModEntry modEntry) {
            logger = modEntry.Logger;
            ModEntry = modEntry;
            ModPath = modEntry.Path;
            modEntry.OnUnload = OnUnload;
            modEntry.OnUpdate = OnUpdate;

            harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll();

            Log("Wrath Tactics loaded.");
            return true;
        }

        static void OnUpdate(UnityModManager.ModEntry modEntry, float delta) {
            try {
                if (Game.Instance?.Player == null) return;
                float gameTime = (float)Game.Instance.Player.GameTime.TotalSeconds;
                Engine.TacticsEvaluator.Tick(gameTime);
            } catch (Exception ex) {
                Error(ex, "[Tactics] Tick error");
            }
        }

        static bool OnUnload(UnityModManager.ModEntry modEntry) {
            harmony.UnpatchAll(modEntry.Info.Id);
            return true;
        }

        public static void Log(string msg) => logger.Log(msg);

        [System.Diagnostics.Conditional("DEBUG")]
        public static void Debug(string msg) => logger.Log(msg);

        public static void Error(string msg) => logger.Error(msg);

        public static void Error(Exception ex, string context = null) {
            if (context != null) logger.Error(context);
            logger.LogException(ex);
        }
    }
}
