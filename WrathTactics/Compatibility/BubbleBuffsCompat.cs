using UnityModManagerNet;
using WrathTactics.Logging;

namespace WrathTactics.Compatibility {
    public static class BubbleBuffsCompat {
        static bool? isBubbleBuffsInstalled;

        public static bool IsInstalled() {
            if (!isBubbleBuffsInstalled.HasValue) {
                isBubbleBuffsInstalled = UnityModManager.FindMod("BuffIt2TheLimit") != null;
                if (isBubbleBuffsInstalled.Value)
                    Log.Compat.Info("BubbleBuffs (BuffIt2TheLimit) detected");
            }
            return isBubbleBuffsInstalled.Value;
        }

        public static bool IsExecuting() {
            if (!IsInstalled()) return false;
            // Rely on unit.Commands.IsRunning() check in TacticsEvaluator
            // for now — BubbleBuffs commands show up there too
            return false;
        }
    }
}
