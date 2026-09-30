using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using RevitScheduleSync.Models.Import;

namespace RevitScheduleSync.UI.ViewModels
{
    /// <summary>
    /// Import step 2: result of the three-way comparison. Read-only overview; the full review
    /// (per-item decisions) and Apply arrive in later phases.
    /// </summary>
    public sealed class ImportAnalysisViewModel : ViewModelBase
    {
        public const string AllStatuses = "All statuses";

        private readonly List<ChangeItemViewModel> _items;
        private bool _showUnchanged;
        private string _statusFilter = AllStatuses;

        public ImportAnalysisViewModel(ChangeSet changes, Action back, Action reanalyse, Action<string, Exception> reportError)
        {
            Changes = changes;
            _items = changes.Items.Select(i => new ChangeItemViewModel(i)).ToList();

            Items = CollectionViewSource.GetDefaultView(_items);
            Items.Filter = o => o is ChangeItemViewModel item && IsVisible(item);

            StatusFilters = new[] { AllStatuses }
                .Concat(_items.Select(i => i.StatusText).Distinct().OrderBy(s => s))
                .ToList();

            BackCommand = new RelayCommand(back, ex => reportError("Could not go back.", ex));
            ReanalyseCommand = new RelayCommand(reanalyse, ex => reportError("The workbook could not be analysed.", ex));

            // Review decisions (Phase 6) and writing to the model (Phase 7) are not implemented yet.
            ApplyCommand = new RelayCommand(() => { }, ex => reportError("Could not apply.", ex), () => false);
        }

        public ChangeSet Changes { get; }

        public ICollectionView Items { get; }

        public IReadOnlyList<string> StatusFilters { get; }

        public string FileName => Path.GetFileName(Changes.Workbook.FilePath);

        public string ExportedText => Changes.Workbook.ExportedUtc.HasValue
            ? $"Exported {Changes.Workbook.ExportedUtc.Value.ToLocalTime():g} from \"{Changes.Workbook.Fingerprint.Title}\""
            : $"Exported from \"{Changes.Workbook.Fingerprint.Title}\"";

        public bool IsBlocked => Changes.IsBlocked;

        public bool IsUncertain => Changes.Fingerprint.Match == FingerprintMatch.Probable;

        public string FingerprintText => string.Join(Environment.NewLine, Changes.Fingerprint.Reasons);

        public int ChangeCount => Changes.Count(ChangeStatus.Change);
        public int TypeChangeCount => Changes.Count(ChangeStatus.TypeChange);
        public int ConflictCount => Changes.Count(ChangeStatus.Conflict);

        public int WarningCount => Changes.Count(ChangeStatus.Invalid) + Changes.Count(ChangeStatus.ReadOnly)
                                   + Changes.Count(ChangeStatus.MissingElement) + Changes.Count(ChangeStatus.MissingParameter);

        public int BlockedCount => Changes.Count(ChangeStatus.Blocked);

        public int UnchangedCount => Changes.Count(ChangeStatus.NoChange);

        public string InfoText =>
            $"{UnchangedCount:N0} unchanged · {Changes.Count(ChangeStatus.RevitChanged)} changed in Revit only · " +
            $"{Changes.Count(ChangeStatus.SameChange)} already equal · {Changes.Count(ChangeStatus.Duplicate)} duplicate · " +
            $"analysed in {Changes.ElapsedMilliseconds:N0} ms";

        public IReadOnlyList<ImportIssue> Issues => Changes.Issues;

        public bool HasIssues => Changes.Issues.Count > 0;

        public bool ShowUnchanged
        {
            get => _showUnchanged;
            set
            {
                if (SetProperty(ref _showUnchanged, value))
                {
                    Items.Refresh();
                }
            }
        }

        public string StatusFilter
        {
            get => _statusFilter;
            set
            {
                if (SetProperty(ref _statusFilter, value ?? AllStatuses))
                {
                    Items.Refresh();
                }
            }
        }

        public ICommand BackCommand { get; }

        public ICommand ReanalyseCommand { get; }

        public ICommand ApplyCommand { get; }

        private bool IsVisible(ChangeItemViewModel item)
        {
            if (_statusFilter != AllStatuses)
            {
                return item.StatusText == _statusFilter;
            }

            // Default view: changes and problems only.
            return _showUnchanged || item.Item.Status != ChangeStatus.NoChange;
        }
    }

    public sealed class ChangeItemViewModel
    {
        public ChangeItemViewModel(ChangeItem item)
        {
            Item = item;
        }

        public ChangeItem Item { get; }

        public ChangeStatus Status => Item.Status;

        public string StatusText => Item.Status switch
        {
            ChangeStatus.NoChange => "No change",
            ChangeStatus.Change => "Change",
            ChangeStatus.TypeChange => "Type change",
            ChangeStatus.RevitChanged => "Revit changed",
            ChangeStatus.SameChange => "Same change",
            ChangeStatus.Conflict => "Conflict",
            ChangeStatus.ReadOnly => "Read only",
            ChangeStatus.Invalid => "Invalid",
            ChangeStatus.MissingElement => "Missing element",
            ChangeStatus.MissingParameter => "Missing parameter",
            ChangeStatus.Blocked => "Blocked",
            ChangeStatus.Duplicate => "Duplicate",
            _ => Item.Status.ToString()
        };

        public string Schedule => Item.ScheduleName;
        public string Element => Item.ElementLabel;
        public long ElementId => Item.ElementId;
        public string Parameter => Item.ParameterName;
        public string Scope => Item.FieldId < 0 ? "–" : Item.IsType ? "Type" : "Instance";
        public string Original => Item.Original;
        public string Current => Item.Current;
        public string Excel => Item.Excel;

        public string Message => Item.AffectedInstances.HasValue
            ? $"Type \"{Item.TypeName}\": affects {Item.AffectedInstances} instance(s). {Item.Message}".Trim()
            : Item.Message;

        public string Location => Item.ExcelRow.HasValue ? $"{Item.SheetName}, row {Item.ExcelRow}" : Item.SheetName;
    }
}
