namespace WrathTactics.Logging {
    /// <summary>
    /// Static category accessors. Usage:
    ///   Log.Engine.Info("Combat started");
    ///   Log.UI.Debug("Panel opened");
    ///   Log.Persistence.Error(ex, "Failed to load config");
    /// </summary>
    public static class Log {
        public static readonly Logger Engine = new Logger("Engine");
        public static readonly Logger UI = new Logger("UI");
        public static readonly Logger Persistence = new Logger("Persist");
        public static readonly Logger Compat = new Logger("Compat");
        public static readonly Logger Game = new Logger("Game");
    }
}
