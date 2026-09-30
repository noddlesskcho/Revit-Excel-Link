using System.Collections.Generic;

namespace RevitScheduleSync.UI.ViewModels
{
    /// <summary>
    /// One top-level area of the window (Export / Import / History).
    /// Phase 0: describes the planned workflow steps only. Later phases replace
    /// each section with a dedicated view model and view.
    /// </summary>
    public class WorkflowSectionViewModel : ViewModelBase
    {
        public WorkflowSectionViewModel(string title, string description, IReadOnlyList<string> steps, string availability)
        {
            Title = title;
            Description = description;
            Steps = steps;
            Availability = availability;
        }

        public string Title { get; }

        public string Description { get; }

        public IReadOnlyList<string> Steps { get; }

        public string Availability { get; }

        public static IReadOnlyList<WorkflowSectionViewModel> CreatePhase0Sections() =>
        [
            new WorkflowSectionViewModel(
                "Export",
                "Select schedules from this project and export them to an Excel workbook for editing.",
                ["Select Schedules", "Analyse", "Export"],
                "Schedule browser arrives in Phase 1."),
            new WorkflowSectionViewModel(
                "Import",
                "Load an edited workbook, review every change against the current model, then apply only what you approve.",
                ["Select Workbook", "Analyse", "Compare", "Validate", "Apply", "Report"],
                "Import workflow arrives in Phase 5."),
            new WorkflowSectionViewModel(
                "History",
                "Reports of previous imports into this project.",
                ["Import Reports"],
                "Import history arrives with the import report (Phase 7).")
        ];
    }
}
