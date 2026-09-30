using RevitScheduleSync.Models.Export;
using RevitScheduleSync.Services;

namespace RevitScheduleSync.Excel
{
    /// <summary>
    /// Writes an ExportWorkbook to an .xlsx file. The ClosedXML implementation can be replaced
    /// (e.g. by Open XML SDK streaming) if very large schedules need it.
    /// Implementations must not call the Revit API.
    /// </summary>
    public interface IWorkbookWriter
    {
        /// <exception cref="ScheduleSyncException">File cannot be written (locked, no access, bad path, too large).</exception>
        void Write(ExportWorkbook workbook, ExportOptions options);
    }
}
