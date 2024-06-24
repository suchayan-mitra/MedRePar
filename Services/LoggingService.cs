using NLog;

namespace MedRePar.Services
{
    public static class LoggingService
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        public static void LogInfo(string message)
        {
            Logger.Info(message);
        }

        public static void LogError(string message, Exception ex = null)
        {
            if (ex != null)
            {
                Logger.Error(ex, message);
            }
            else
            {
                Logger.Error(message);
            }
        }

        public static void LogDebug(string message)
        {
            Logger.Debug(message);
        }

        public static void LogWarn(string message)
        {
            Logger.Warn(message);
        }
    }
}
