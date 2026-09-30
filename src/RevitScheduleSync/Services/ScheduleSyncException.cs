using System;

namespace RevitScheduleSync.Services
{
    /// <summary>
    /// An expected, user-explainable failure (bad workbook, wrong project, locked file ...).
    /// Its Message is shown to the user as-is, so it must be written for Revit users.
    /// </summary>
    public class ScheduleSyncException : Exception
    {
        public ScheduleSyncException(string userMessage)
            : base(userMessage)
        {
        }

        public ScheduleSyncException(string userMessage, Exception innerException)
            : base(userMessage, innerException)
        {
        }
    }
}
