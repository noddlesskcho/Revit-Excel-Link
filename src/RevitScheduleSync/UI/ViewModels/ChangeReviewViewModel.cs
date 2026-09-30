using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using RevitScheduleSync.Models.Import;
using RevitScheduleSync.Services;

namespace RevitScheduleSync.UI.ViewModels
{
    /// <summary>
    /// Import step 2: review the three-way comparison and decide per item what happens.
    /// Only simple changes are pre-approved; type changes and conflicts need an explicit decision.
    /// Nothing is written here: the Apply step (Phase 7) writes only items with WillWrite.
    /// </summary>
    public sealed class ChangeReviewViewModel : ViewModelBase
    {
        private readonly List<ChangeRowViewModel> _rows;
        private readonly IUserPrompts _prompts;
        private ReviewFilter _selectedFilter;
        private ChangeRowViewModel? _selectedRow;

        public ChangeReviewViewModel(
            ChangeSet changes, IUserPrompts prompts, Action back, Action reanalyse, Action<string, Exception> reportError)
        {
            Changes = changes;
            _prompts = prompts;
            _rows = changes.Items.Select(i => new ChangeRowViewModel(i, this)).ToList();

            Filters = ReviewFilter.All;
            _selectedFilter = Filters[0];

            Items = CollectionViewSource.GetDefaultView(_rows);
            Items.Filter = o => o is ChangeRowViewModel row && _selectedFilter.Matches(row.Item);

            BackCommand = new RelayCommand(back, ex => reportError("Could not go back.", ex));
            ReanalyseCommand = new RelayCommand(reanalyse, ex => reportError("The workbook could not be analysed.", ex));
            ShowFilterCommand = new ParameterCommand(name => SelectedFilter = Filters.FirstOrDefault(f => f.Name == name) ?? Filters[0]);

            ApplyAllChangesCommand = new RelayCommand(() => SetAll(ChangeStatus.Change, ReviewDecision.Apply),
                ex => reportError("Could not update decisions.", ex), () => ChangeCount > 0);
            SkipAllChangesCommand = new RelayCommand(SkipAll,
                ex => reportError("Could not update decisions.", ex), () => ChangeCount + TypeChangeCount > 0);
            ApproveTypeChangesCommand = new RelayCommand(ApproveAllTypeChanges,
                ex => reportError("Could not update decisions.", ex),
                () => _rows.Any(r => r.Item.Status == ChangeStatus.TypeChange && r.Item.Decision != ReviewDecision.Apply));
            KeepRevitForConflictsCommand = new RelayCommand(KeepRevitForConflicts,
                ex => reportError("Could not update decisions.", ex),
                () => _rows.Any(r => r.Item.ConflictKind == ConflictKind.ThreeWay && r.Item.Decision != ReviewDecision.KeepRevit));

            // Writing to the model arrives in Phase 7.
            ApplyCommand = new RelayCommand(() => { }, ex => reportError("Could not apply.", ex), () => false);
        }

        public ChangeSet Changes { get; }

        public ICollectionView Items { get; }

        public IReadOnlyList<ReviewFilter> Filters { get; }

        public ReviewFilter SelectedFilter
        {
            get => _selectedFilter;
            set
            {
                if (value != null && SetProperty(ref _selectedFilter, value))
                {
                    Items.Refresh();
                    OnPropertyChanged(nameof(VisibleText));
                }
            }
        }

        public ChangeRowViewModel? SelectedRow
        {
            get => _selectedRow;
            set => SetProperty(ref _selectedRow, value);
        }

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

        public int WarningCount => _rows.Count(r => ReviewFilter.IsWarning(r.Item.Status));

        public int BlockedCount => Changes.Count(ChangeStatus.Blocked);

        /// <summary>Items the Apply step would write with the current decisions.</summary>
        public int WillWriteCount => _rows.Count(r => r.Item.WillWrite);

        /// <summary>Items still waiting for a decision (not applied until decided).</summary>
        public int PendingCount => _rows.Count(r => r.Item.Decision == ReviewDecision.Pending);

        public string ApplyText => $"Apply {WillWriteCount} change(s) ›";

        public string VisibleText => $"{_rows.Count(r => _selectedFilter.Matches(r.Item)):N0} of {_rows.Count:N0} shown";

        public string InfoText =>
            $"{Changes.Count(ChangeStatus.NoChange):N0} unchanged · {Changes.Count(ChangeStatus.RevitChanged)} changed in Revit only · " +
            $"{Changes.Count(ChangeStatus.SameChange)} already equal · {Changes.Count(ChangeStatus.Duplicate)} duplicate · " +
            $"analysed in {Changes.ElapsedMilliseconds:N0} ms";

        public IReadOnlyList<ImportIssue> Issues => Changes.Issues;

        public bool HasIssues => Changes.Issues.Count > 0;

        public ICommand BackCommand { get; }
        public ICommand ReanalyseCommand { get; }
        public ICommand ShowFilterCommand { get; }
        public ICommand ApplyAllChangesCommand { get; }
        public ICommand SkipAllChangesCommand { get; }
        public ICommand ApproveTypeChangesCommand { get; }
        public ICommand KeepRevitForConflictsCommand { get; }
        public ICommand ApplyCommand { get; }

        /// <summary>Shows the type-parameter warning for one item. True when the user approves.</summary>
        internal bool ConfirmTypeChange(ChangeItem item)
        {
            var content = new StringBuilder()
                .AppendLine($"Type: {item.TypeName}")
                .AppendLine($"Parameter: {item.ParameterName}")
                .AppendLine($"Current: {item.Current}")
                .AppendLine($"New: {item.Excel}")
                .AppendLine($"Affected instances: {item.AffectedInstances?.ToString() ?? "unknown"}")
                .AppendLine()
                .Append("Every element of this type changes, including elements that are not in the workbook.")
                .ToString();

            return _prompts.Confirm("TYPE PARAMETER CHANGE", content, "Approve type change", "Cancel");
        }

        internal void DecisionsChanged()
        {
            OnPropertyChanged(nameof(WillWriteCount));
            OnPropertyChanged(nameof(PendingCount));
            OnPropertyChanged(nameof(ApplyText));
            CommandManager.InvalidateRequerySuggested();

            if (_selectedFilter.DependsOnDecisions)
            {
                // Deferred: refreshing while the row's ComboBox is still committing its selection is not allowed.
                Dispatcher.CurrentDispatcher.BeginInvoke(() =>
                {
                    Items.Refresh();
                    OnPropertyChanged(nameof(VisibleText));
                });
            }
        }

        private void SetAll(ChangeStatus status, ReviewDecision decision)
        {
            foreach (var row in _rows.Where(r => r.Item.Status == status))
            {
                row.SetDecision(decision);
            }

            DecisionsChanged();
        }

        private void SkipAll()
        {
            foreach (var row in _rows.Where(r => r.Item.Status is ChangeStatus.Change or ChangeStatus.TypeChange))
            {
                row.SetDecision(ReviewDecision.Skip);
            }

            DecisionsChanged();
        }

        private void ApproveAllTypeChanges()
        {
            var pending = _rows.Where(r => r.Item.Status == ChangeStatus.TypeChange && r.Item.Decision != ReviewDecision.Apply).ToList();
            var lines = pending.Take(8).Select(r =>
                $"• {r.Item.TypeName} — {r.Item.ParameterName}: {r.Item.Current} → {r.Item.Excel} ({r.Item.AffectedInstances?.ToString() ?? "?"} instances)");
            var content = string.Join(Environment.NewLine, lines)
                          + (pending.Count > 8 ? $"{Environment.NewLine}… and {pending.Count - 8} more" : string.Empty)
                          + $"{Environment.NewLine}{Environment.NewLine}Total affected instances: {pending.Sum(r => r.Item.AffectedInstances ?? 0):N0}"
                          + $"{Environment.NewLine}Every element of these types changes, including elements not in the workbook.";

            if (_prompts.Confirm($"TYPE PARAMETER CHANGES ({pending.Count})", content, $"Approve {pending.Count} type change(s)", "Cancel"))
            {
                foreach (var row in pending)
                {
                    row.SetDecision(ReviewDecision.Apply);
                }

                DecisionsChanged();
            }
        }

        private void KeepRevitForConflicts()
        {
            foreach (var row in _rows.Where(r => r.Item.ConflictKind == ConflictKind.ThreeWay))
            {
                row.SetDecision(ReviewDecision.KeepRevit);
            }

            DecisionsChanged();
        }
    }

    /// <summary>A choice in a row's Action drop-down.</summary>
    public sealed record ActionOption(string Text, ReviewDecision Decision);

    public sealed class ChangeRowViewModel : ViewModelBase
    {
        private readonly ChangeReviewViewModel _owner;

        public ChangeRowViewModel(ChangeItem item, ChangeReviewViewModel owner)
        {
            Item = item;
            _owner = owner;
            Actions = CreateActions(item);
        }

        public ChangeItem Item { get; }

        public IReadOnlyList<ActionOption> Actions { get; }

        public bool HasActions => Actions.Count > 0;

        public ActionOption? SelectedAction
        {
            get => Actions.FirstOrDefault(a => a.Decision == Item.Decision);
            set
            {
                if (value == null || value.Decision == Item.Decision)
                {
                    return;
                }

                // Type values affect every instance: approval always goes through the warning dialog.
                bool writesType = Item.IsType && value.Decision is ReviewDecision.Apply or ReviewDecision.UseExcel;
                if (writesType && !_owner.ConfirmTypeChange(Item))
                {
                    // Re-sync the ComboBox after WPF finishes committing the rejected selection.
                    Dispatcher.CurrentDispatcher.BeginInvoke(() => OnPropertyChanged(nameof(SelectedAction)));
                    return;
                }

                SetDecision(value.Decision);
                _owner.DecisionsChanged();
            }
        }

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
            ? $"Type \"{Item.TypeName}\": {Item.AffectedInstances} instance(s). {Item.Message}".Trim()
            : Item.Message;

        public bool NeedsDecision => Item.Decision == ReviewDecision.Pending;

        /// <summary>Full explanation for the details panel.</summary>
        public string DetailText
        {
            get
            {
                var text = new StringBuilder();
                if (Item.IsType && Item.FieldId >= 0)
                {
                    text.AppendLine($"TYPE PARAMETER · Type: {Item.TypeName} · Affected instances: {Item.AffectedInstances?.ToString() ?? "–"}");
                }

                text.AppendLine($"{Item.ElementLabel} (id {Item.ElementId}) · {Item.ParameterName} · " +
                                (Item.ExcelRow.HasValue ? $"sheet \"{Item.SheetName}\", row {Item.ExcelRow}" : $"sheet \"{Item.SheetName}\""));
                text.AppendLine($"Original: {Show(Item.Original)}   ·   Current Revit: {Show(Item.Current)}   ·   Excel: {Show(Item.Excel)}");
                if (!string.IsNullOrEmpty(Item.Message))
                {
                    text.AppendLine(Item.Message);
                }

                return text.ToString().TrimEnd();
            }
        }

        internal void SetDecision(ReviewDecision decision)
        {
            if (Actions.Any(a => a.Decision == decision) && Item.Decision != decision)
            {
                Item.Decision = decision;
                OnPropertyChanged(nameof(SelectedAction));
                OnPropertyChanged(nameof(NeedsDecision));
            }
        }

        private static string Show(string value) => value.Length == 0 ? "(blank)" : value;

        private static IReadOnlyList<ActionOption> CreateActions(ChangeItem item) => item.Status switch
        {
            ChangeStatus.Change => [new("Apply", ReviewDecision.Apply), new("Skip", ReviewDecision.Skip)],
            ChangeStatus.TypeChange =>
            [
                new("Needs approval", ReviewDecision.Pending),
                new("Approve type change", ReviewDecision.Apply),
                new("Skip", ReviewDecision.Skip)
            ],
            ChangeStatus.Conflict when item.ConflictKind == ConflictKind.ThreeWay && item.Target != null =>
            [
                new("Decide…", ReviewDecision.Pending),
                new("Keep Revit", ReviewDecision.KeepRevit),
                new("Use Excel", ReviewDecision.UseExcel)
            ],
            ChangeStatus.Conflict when item.ConflictKind == ConflictKind.ThreeWay => [new("Keep Revit", ReviewDecision.KeepRevit)],
            ChangeStatus.Conflict => [new("Skip — fix in Excel", ReviewDecision.Skip)],
            _ => []
        };
    }

    /// <summary>A named view of the change set. Cards and the drop-down both select one.</summary>
    public sealed class ReviewFilter
    {
        private readonly Func<ChangeItem, bool> _predicate;

        private ReviewFilter(string name, Func<ChangeItem, bool> predicate, bool dependsOnDecisions = false)
        {
            Name = name;
            _predicate = predicate;
            DependsOnDecisions = dependsOnDecisions;
        }

        public string Name { get; }

        public bool DependsOnDecisions { get; }

        public bool Matches(ChangeItem item) => _predicate(item);

        public override string ToString() => Name;

        public static bool IsWarning(ChangeStatus status) =>
            status is ChangeStatus.Invalid or ChangeStatus.ReadOnly or ChangeStatus.MissingElement or ChangeStatus.MissingParameter;

        /// <summary>First entry is the default: changes and problems, no unchanged values.</summary>
        public static readonly IReadOnlyList<ReviewFilter> All =
        [
            new("Changes and problems", i => i.Status != ChangeStatus.NoChange),
            new("Needs decision", i => i.Decision == ReviewDecision.Pending, dependsOnDecisions: true),
            new("Will be applied", i => i.WillWrite, dependsOnDecisions: true),
            new("Changes", i => i.Status is ChangeStatus.Change or ChangeStatus.TypeChange),
            new("Type changes", i => i.Status == ChangeStatus.TypeChange),
            new("Conflicts", i => i.Status == ChangeStatus.Conflict),
            new("Warnings", i => IsWarning(i.Status)),
            new("Blocked", i => i.Status == ChangeStatus.Blocked),
            new("Revit changed / same / duplicate", i => i.Status is ChangeStatus.RevitChanged or ChangeStatus.SameChange or ChangeStatus.Duplicate),
            new("Unchanged", i => i.Status == ChangeStatus.NoChange),
            new("Everything", _ => true)
        ];
    }

    /// <summary>ICommand with a string parameter (summary-card clicks).</summary>
    public sealed class ParameterCommand : ICommand
    {
        private readonly Action<string> _execute;

        public ParameterCommand(Action<string> execute)
        {
            _execute = execute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _execute(parameter as string ?? string.Empty);
    }
}
