using System;
using System.Diagnostics;
using Autodesk.Revit.UI;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Services;

namespace RevitScheduleSync.Commands
{
    /// <summary>
    /// Exception boundary for every external command: nothing thrown inside a command
    /// may escape into Revit. Logs start/end/duration and shows a readable error on failure.
    /// </summary>
    internal static class CommandGuard
    {
        public static Result Run(string commandName, ILogger logger, Func<Result> body)
        {
            logger.Info($"Command start: {commandName}");
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var result = body();
                logger.Info($"Command end: {commandName} -> {result} ({stopwatch.ElapsedMilliseconds} ms)");
                return result;
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                logger.Info($"Command cancelled by user: {commandName}");
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                logger.Error($"Command failed: {commandName} ({stopwatch.ElapsedMilliseconds} ms)", ex);
                ErrorReporter.Show(logger, "Schedule Sync could not complete the command.", ex);

                // The error has already been shown to the user. Returning Failed would make
                // Revit show a second, less helpful dialog, so report the command as cancelled.
                return Result.Cancelled;
            }
        }
    }
}
