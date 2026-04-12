using System;
using HarmonyLib;
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

            harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll();

            Log("Wrath Tactics loaded.");
            return true;
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
