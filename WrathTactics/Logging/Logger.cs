using System;
using UnityModManagerNet;

namespace WrathTactics.Logging {
    public enum LogLevel { Trace, Debug, Info, Warn, Error }

    /// <summary>
    /// Per-category logger. Writes to session file for all levels.
    /// Warn/Error additionally write to UMM ModLogger (visible in UMM console + Player.log).
    /// </summary>
    public class Logger {
        readonly string category;

        public Logger(string category) {
            this.category = category;
        }

        public void Trace(string msg) => Write(LogLevel.Trace, msg);
        public void Debug(string msg) => Write(LogLevel.Debug, msg);
        public void Info(string msg) => Write(LogLevel.Info, msg);
        public void Warn(string msg) => Write(LogLevel.Warn, msg);
        public void Error(string msg) => Write(LogLevel.Error, msg);

        public void Error(Exception ex, string context = null) {
            if (context != null) Write(LogLevel.Error, context);
            if (ex == null) return;
            Write(LogLevel.Error, $"  {ex.GetType().Name}: {ex.Message}");
            if (!string.IsNullOrEmpty(ex.StackTrace)) {
                var lines = ex.StackTrace.Split('\n');
                int max = Math.Min(10, lines.Length);
                for (int i = 0; i < max; i++) {
                    Write(LogLevel.Error, $"  {lines[i].TrimEnd()}");
                }
                if (lines.Length > max)
                    Write(LogLevel.Error, $"  ... ({lines.Length - max} more frames)");
            }
        }

        void Write(LogLevel level, string msg) {
            string levelStr = FormatLevel(level);
            string stamp = DateTime.Now.ToString("HH:mm:ss.fff");
            string line = $"[{stamp}] [{levelStr}] [{category}] {msg}";
            DebugLog.WriteLine(line);

            if (level == LogLevel.Warn || level == LogLevel.Error) {
                var ml = Main.ModEntry?.Logger;
                if (ml != null) {
                    if (level == LogLevel.Error) ml.Error(line);
                    else ml.Log(line);
                }
            }
        }

        static string FormatLevel(LogLevel level) {
            switch (level) {
                case LogLevel.Trace: return "TRACE";
                case LogLevel.Debug: return "DEBUG";
                case LogLevel.Info: return "INFO ";
                case LogLevel.Warn: return "WARN ";
                case LogLevel.Error: return "ERROR";
                default: return "?????";
            }
        }
    }
}
