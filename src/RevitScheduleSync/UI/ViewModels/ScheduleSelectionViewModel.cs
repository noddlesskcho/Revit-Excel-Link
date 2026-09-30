using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Models;

namespace RevitScheduleSync.UI.ViewModels
{
    /// <summary>Export step 1: schedule browser (search, select).</summary>
    public sealed class ScheduleSelectionViewModel : ViewModelBase
    {
        private readonly ILogger _logger;
        private readonly List<ScheduleRowViewModel> _rows;
        private string _searchText = string.Empty;
        private bool _showUnsupported;

        public ScheduleSelectionViewModel(
            ScheduleScanResult scan, ILogger logger, Action<string, Exception> reportError, Action analyse)
        {
            _logger = logger;
            _rows = scan.Schedules.Select(s => new ScheduleRowViewModel(s, OnSelectionChanged)).ToList();

            Schedules = CollectionViewSource.GetDefaultView(_rows);
            Schedules.Filter = item => item is ScheduleRowViewModel row && IsVisible(row);

            UnsupportedCount = _rows.Count(r => !r.IsSelectable);
            ExcludedText = scan.ExcludedCounts.Count == 0
                ? string.Empty
                : "Not listed: " + string.Join(", ", scan.ExcludedCounts.Select(e => $"{e.Key.ToLowerInvariant()} ({e.Value})"));

            SelectAllCommand = new RelayCommand(SelectAllVisible, ex => reportError("Could not select schedules.", ex));
            ClearAllCommand = new RelayCommand(ClearAll, ex => reportError("Could not clear the selection.", ex));
            ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty, ex => reportError("Could not clear the search.", ex));

            AnalyseCommand = new RelayCommand(analyse, ex => reportError("Could not analyse schedules.", ex), () => SelectedCount > 0);
        }

        public ICollectionView Schedules { get; }

        public int TotalCount => _rows.Count;

        public int UnsupportedCount { get; }

        public string ExcludedText { get; }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value ?? string.Empty))
                {
                    RefreshFilter();
                }
            }
        }

        public bool ShowUnsupported
        {
            get => _showUnsupported;
            set
            {
                if (SetProperty(ref _showUnsupported, value))
                {
                    RefreshFilter();
                }
            }
        }

        public int VisibleCount => _rows.Count(IsVisible);

        public int SelectedCount => _rows.Count(r => r.IsSelected);

        public int SelectedItemisedCount => _rows.Count(r => r.IsSelected && r.Support == ScheduleSupport.EditableSync);

        public int SelectedGroupedCount => _rows.Count(r => r.IsSelected && r.Support == ScheduleSupport.PresentationOnly);

        public string SelectionText => SelectedCount == 0
            ? "No schedules selected"
            : $"{SelectedCount} selected · {SelectedItemisedCount} itemised (editable) · {SelectedGroupedCount} grouped (presentation only)";

        public string ListSummaryText => VisibleCount == TotalCount
            ? $"{TotalCount} schedules"
            : $"{VisibleCount} of {TotalCount} schedules shown";

        public bool IsEmpty => _rows.Count == 0;

        /// <summary>Selected schedules in display order; input for Phase 2.</summary>
        public IReadOnlyList<ScheduleSummary> SelectedSchedules =>
            _rows.Where(r => r.IsSelected).Select(r => r.Summary).ToList();

        public ICommand SelectAllCommand { get; }

        public ICommand ClearAllCommand { get; }

        public ICommand ClearSearchCommand { get; }

        public ICommand AnalyseCommand { get; }

        private bool IsVisible(ScheduleRowViewModel row) =>
            (_showUnsupported || row.IsSelectable) && row.Matches(_searchText);

        /// <summary>Selects only rows currently visible (respects search) and selectable.</summary>
        private void SelectAllVisible()
        {
            foreach (var row in _rows.Where(r => r.IsSelectable && IsVisible(r)))
            {
                row.IsSelected = true;
            }

            _logger.Debug($"Select all: {SelectedCount} selected.");
        }

        private void ClearAll()
        {
            foreach (var row in _rows)
            {
                row.IsSelected = false;
            }
        }

        private void RefreshFilter()
        {
            Schedules.Refresh();
            OnPropertyChanged(nameof(VisibleCount));
            OnPropertyChanged(nameof(ListSummaryText));
        }

        private void OnSelectionChanged()
        {
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(SelectedItemisedCount));
            OnPropertyChanged(nameof(SelectedGroupedCount));
            OnPropertyChanged(nameof(SelectionText));
        }
    }
}
