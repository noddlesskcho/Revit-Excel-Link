using System.Collections.Generic;

namespace RevitScheduleSync.UI.ViewModels
{
    /// <summary>
    /// Placeholder section that describes a planned workflow.
    /// Replaced by a dedicated view model when its phase is implemented.
    /// </summary>
    public sealed class WorkflowSectionViewModel : SectionViewModel
    {
        public WorkflowSectionViewModel(string title, string description, IReadOnlyList<string> steps, string availability)
            : base(title)
        {
            Description = description;
            Steps = steps;
            Availability = availability;
        }

        public string Description { get; }

        public IReadOnlyList<string> Steps { get; }

        public string Availability { get; }

        public static WorkflowSectionViewModel CreateImport() => new(
            "Import",
            "Load an edited workbook, review every change against the current model, then apply only what you approve.",
            ["Select Workbook", "Analyse", "Compare", "Validate", "Apply", "Report"],
            "Import workflow arrives in Phase 5.");

        public static WorkflowSectionViewModel CreateHistory() => new(
            "History",
            "Reports of previous imports into this project.",
            ["Import Reports"],
            "Import history arrives with the import report (Phase 7).");
    }
}
