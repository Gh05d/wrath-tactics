using System;
using System.IO;
using System.Linq;

namespace WrathTactics.Logging {
    /// <summary>
    /// Session-scoped file logger. Owns a single StreamWriter opened at Init()
    /// and closed at Shutdown(). Rotates: keeps last 10 session files.
    /// </summary>
    public static class DebugLog {
        const int MaxSessionFiles = 10;

        static StreamWriter writer;
        static readonly object lockObj = new object();

        public static string CurrentSessionPath { get; private set; }

        public static void Init(string modPath) {
            try {
                var logsDir = Path.Combine(modPath, "Logs");
                Directory.CreateDirectory(logsDir);

                RotateOldFiles(logsDir);

                var stamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
                CurrentSessionPath = Path.Combine(logsDir, $"wrath-tactics-{stamp}.log");

                writer = new StreamWriter(CurrentSessionPath, append: false) {
                    AutoFlush = true,
                };
                writer.WriteLine($"=== Wrath Tactics session started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            } catch (Exception ex) {
                writer = null;
                UnityEngine.Debug.LogError($"[WrathTactics] DebugLog.Init failed: {ex}");
            }
        }

        public static void Shutdown() {
            lock (lockObj) {
                if (writer == null) return;
                try {
                    writer.WriteLine($"=== Session ended at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                    writer.Flush();
                    writer.Close();
                } catch { }
                writer = null;
            }
        }

        /// <summary>
        /// Writes a pre-formatted line to the session file. Thread-safe no-op if writer is null.
        /// </summary>
        public static void WriteLine(string line) {
            lock (lockObj) {
                if (writer == null) return;
                try {
                    writer.WriteLine(line);
                } catch { }
            }
        }

        static void RotateOldFiles(string logsDir) {
            try {
                var files = Directory.GetFiles(logsDir, "wrath-tactics-*.log")
                    .OrderBy(f => File.GetCreationTimeUtc(f))
                    .ToList();
                while (files.Count >= MaxSessionFiles) {
                    try { File.Delete(files[0]); } catch { }
                    files.RemoveAt(0);
                }
            } catch { }
        }
    }
}
