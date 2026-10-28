using RevitScheduleSync.Models.Import;
using RevitScheduleSync.Services;

namespace RevitScheduleSync.Excel
{
    /// <summary>
    /// Reads a Revit-Excel Link workbook (metadata + current cell values). Never calls the Revit API.
    /// </summary>
    public interface IWorkbookReader
    {
        /// <exception cref="ScheduleSyncException">Not a Revit-Excel Link workbook, unsupported version, unreadable file.</exception>
        ImportWorkbook Read(string path);
    }
}
