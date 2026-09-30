using System;
using RevitScheduleSync.Models;

namespace RevitScheduleSync.UI.ViewModels
{
    /// <summary>One schedule in the browser grid.</summary>
    public sealed class ScheduleRowViewModel : ViewModelBase
    {
        private readonly Action _selectionChanged;
        private bool _isSelected;

        public ScheduleRowViewModel(ScheduleSummary summary, Action selectionChanged)
        {
            Summary = summary;
            _selectionChanged = selectionChanged;
        }

        public ScheduleSummary Summary { get; }

        public string Name => Summary.Name;

        public string CategoryName => Summary.CategoryName;

        public bool IsSelectable => Summary.Support != ScheduleSupport.Unsupported;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value && IsSelectable))
                {
                    _selectionChanged();
                }
            }
        }

        public string ModeText => Summary.IsItemized ? "Itemised" : "Grouped";

        public string KindText => Summary.Kind switch
        {
            ScheduleKind.Regular => "Schedule",
            ScheduleKind.MultiCategory => "Multi-category",
            ScheduleKind.KeySchedule => "Key schedule",
            ScheduleKind.MaterialTakeoff => "Material takeoff",
            ScheduleKind.NoteBlock => "Note block",
            ScheduleKind.SheetList => "Sheet list",
            ScheduleKind.ViewList => "View list",
            _ => Summary.Kind.ToString()
        };

        /// <summary>Sort key for the Fields column (visible fields).</summary>
        public int VisibleFieldCount => Summary.FieldCount - Summary.HiddenFieldCount;

        public string FieldsText => Summary.HiddenFieldCount == 0
            ? Summary.FieldCount.ToString()
            : $"{Summary.FieldCount} ({Summary.HiddenFieldCount} hidden)";

        /// <summary>Sort key for the Elements column; unknown counts sort first.</summary>
        public int ElementCountSortKey => Summary.EstimatedElementCount ?? -1;

        public string ElementCountText => Summary.EstimatedElementCount?.ToString("N0") ?? "–";

        public string SupportText => Summary.Support switch
        {
            ScheduleSupport.EditableSync => "ITEMISED — editable sync",
            ScheduleSupport.PresentationOnly => "GROUPED — restricted",
            _ => "Not supported"
        };

        public ScheduleSupport Support => Summary.Support;

        /// <summary>Tooltip: support explanation plus any caveats.</summary>
        public string SupportDetail
        {
            get
            {
                var text = Summary.SupportNote;
                if (Summary.IncludesLinkedFiles)
                {
                    text += "\nIncludes elements from linked files: those rows are not exported.";
                }

                if (Summary.IsFilteredBySheet)
                {
                    text += "\nFiltered by sheet: element count may differ from the schedule placed on a sheet.";
                }

                return text;
            }
        }

        public bool Matches(string search) =>
            string.IsNullOrWhiteSpace(search)
            || Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || CategoryName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
            || KindText.Contains(search, StringComparison.CurrentCultureIgnoreCase);
    }
}
