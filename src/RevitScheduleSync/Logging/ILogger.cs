using System;

namespace RevitScheduleSync.Logging
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// Diagnostic log. Implementations must never throw: logging failures must not affect Revit.
    /// </summary>
    public interface ILogger
    {
        void Log(LogLevel level, string message, Exception? exception = null);
    }

    public static class LoggerExtensions
    {
        public static void Debug(this ILogger logger, string message) => logger.Log(LogLevel.Debug, message);

        public static void Info(this ILogger logger, string message) => logger.Log(LogLevel.Info, message);

        public static void Warning(this ILogger logger, string message, Exception? exception = null) =>
            logger.Log(LogLevel.Warning, message, exception);

        public static void Error(this ILogger logger, string message, Exception? exception = null) =>
            logger.Log(LogLevel.Error, message, exception);
    }

    public sealed class NullLogger : ILogger
    {
        public static readonly NullLogger Instance = new();

        private NullLogger()
        {
        }

        public void Log(LogLevel level, string message, Exception? exception = null)
        {
        }
    }
}
