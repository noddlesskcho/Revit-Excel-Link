using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace RevitScheduleSync.Logging
{
    /// <summary>
    /// Appends plain-text log lines to one file per day:
    /// %LOCALAPPDATA%\RevitScheduleSync\Logs\ScheduleSync-yyyyMMdd.log
    /// </summary>
    public sealed class FileLogger : ILogger
    {
        private const int RetentionDays = 30;

        private readonly object _sync = new();
        private readonly LogLevel _minimumLevel;

        public FileLogger(string logDirectory, LogLevel minimumLevel = LogLevel.Debug)
        {
            LogDirectory = logDirectory;
            _minimumLevel = minimumLevel;
        }

        public string LogDirectory { get; }

        public string CurrentLogFile =>
            Path.Combine(LogDirectory, $"ScheduleSync-{DateTime.Now:yyyyMMdd}.log");

        public static string DefaultLogDirectory =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RevitScheduleSync",
                "Logs");

        public void Log(LogLevel level, string message, Exception? exception = null)
        {
            if (level < _minimumLevel)
            {
                return;
            }

            try
            {
                var line = new StringBuilder()
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                    .Append(" [").Append(LevelText(level)).Append("] ")
                    .Append("[T").Append(Environment.CurrentManagedThreadId).Append("] ")
                    .Append(message);

                if (exception != null)
                {
                    // Full exception text (type, message, stack trace, inner exceptions) stays in the log only.
                    line.AppendLine().Append(exception);
                }

                line.AppendLine();

                lock (_sync)
                {
                    Directory.CreateDirectory(LogDirectory);
                    File.AppendAllText(CurrentLogFile, line.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never break the add-in or Revit.
            }
        }

        /// <summary>Deletes log files older than the retention period. Never throws.</summary>
        public void DeleteOldLogs()
        {
            try
            {
                if (!Directory.Exists(LogDirectory))
                {
                    return;
                }

                var cutoff = DateTime.Now.AddDays(-RetentionDays);
                foreach (var file in Directory.EnumerateFiles(LogDirectory, "ScheduleSync-*.log"))
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception ex)
            {
                Log(LogLevel.Warning, "Could not clean up old log files.", ex);
            }
        }

        private static string LevelText(LogLevel level) => level switch
        {
            LogLevel.Debug => "DEBUG",
            LogLevel.Info => "INFO ",
            LogLevel.Warning => "WARN ",
            LogLevel.Error => "ERROR",
            _ => "?????"
        };
    }
}
