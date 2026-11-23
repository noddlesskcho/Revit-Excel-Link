using RevitScheduleSync.Models.Import;
using RevitScheduleSync.Services;

namespace RevitScheduleSync.Excel
{
    /// <summary>
    /// Reads a ScheduleDock workbook (metadata + current cell values). Never calls the Revit API.
    /// </summary>
    public interface IWorkbookReader
    {
        /// <exception cref="ScheduleSyncException">Not a ScheduleDock workbook, unsupported version, unreadable file.</exception>
        ImportWorkbook Read(string path);
    }
}
