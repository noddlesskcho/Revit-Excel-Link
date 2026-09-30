using RevitScheduleSync.Models.Import;
using RevitScheduleSync.Services;

namespace RevitScheduleSync.Excel
{
    /// <summary>
    /// Reads a Schedule Sync workbook (metadata + current cell values). Never calls the Revit API.
    /// </summary>
    public interface IWorkbookReader
    {
        /// <exception cref="ScheduleSyncException">Not a Schedule Sync workbook, unsupported version, unreadable file.</exception>
        ImportWorkbook Read(string path);
    }
}
