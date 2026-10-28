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
    /// Nothing is written here: Apply (after a confirmation) writes only items with WillWrite.
    /// </summary>
    public sealed class ChangeReviewViewModel : ViewModelBase
    {
        private readonly List<ChangeRowViewModel> _rows;
        private readonly IUserPrompts _prompts;
        private readonly Action<string, Exception> _reportError;
        private ReviewFilter _selectedFilter;
        private ScheduleTab _selectedTab;
        private ChangeRowViewModel? _selectedRow;

        public ChangeReviewViewModel(
            ChangeSet changes, IUserPrompts prompts, Action back, Action reanalyse, Action<ChangeReviewViewModel> apply,
            Action<string, Exception> reportError)
        {
            Changes = changes;
            _prompts = prompts;
            _reportError = reportError;
            _rows = changes.Items.Select(i => new ChangeRowViewModel(i, this)).ToList();

            Filters = ReviewFilter.All;
            _selectedFilter = Filters[0];

            ScheduleTabs = ScheduleTab.Create(changes);
            _selectedTab = ScheduleTabs[0];

            Items = CollectionViewSource.GetDefaultView(_rows);
            Items.Filter = o => o is ChangeRowViewModel row && _selectedTab.Contains(row.Item) && _selectedFilter.Matches(row.Item);

            BackCommand = new RelayCommand(back, ex => reportError("Could not go back.", ex));
            ReanalyseCommand = new RelayCommand(reanalyse, ex => reportError("The workbook could not be analysed.", ex));
            ShowFilterCommand = new ParameterCommand(name => SelectedFilter = Filters.FirstOrDefault(f => f.Name == name) ?? Filters[0]);

            ApplyAllChangesCommand = new RelayCommand(() => SetAll(ChangeStatus.Change, ReviewDecision.Apply),
                ex => reportError("Could not update decisions.", ex), () => ChangeCount > 0);
            SkipAllChangesCommand = new RelayCommand(SkipAll,
                ex => reportError("Could not update decisions.", ex), () => ChangeCount + TypeChangeCount > 0);
            ApproveTypeChangesCommand = new RelayCommand(ApproveAllTypeChanges,
                ex => reportError("Could not update decisions.", ex),
                () => Scoped.Any(r => r.Item.Status == ChangeStatus.TypeChange && r.Item.Decision != ReviewDecision.Apply));
            KeepRevitForConflictsCommand = new RelayCommand(KeepRevitForConflicts,
                ex => reportError("Could not update decisions.", ex),
                () => Scoped.Any(r => r.Item.ConflictKind == ConflictKind.ThreeWay && r.Item.Decision != ReviewDecision.KeepRevit));

            ApplyCommand = new RelayCommand(() => apply(this), ex => reportError("The changes could not be applied.", ex),
                () => !IsBlocked && TotalWillWriteCount > 0);
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

        /// <summary>"All schedules" plus one tab per schedule sheet in the workbook.</summary>
        public IReadOnlyList<ScheduleTab> ScheduleTabs { get; }

        /// <summary>Scopes the grid, summary cards and bulk actions. Apply always covers every schedule.</summary>
        public ScheduleTab SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (value != null && SetProperty(ref _selectedTab, value))
                {
                    Items.Refresh();
                    SelectedRow = null;
                    RaiseCountsChanged();
                    CommandManager.InvalidateRequerySuggested();
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

        // Summary cards: scoped to the selected schedule tab.
        public int ChangeCount => Scoped.Count(r => r.Item.Status is ChangeStatus.Change or ChangeStatus.Create);
        public int TypeChangeCount => Scoped.Count(r => r.Item.Status == ChangeStatus.TypeChange);
        public int ConflictCount => Scoped.Count(r => r.Item.Status == ChangeStatus.Conflict);
        public int WarningCount => Scoped.Count(r => ReviewFilter.IsWarning(r.Item.Status));
        public int BlockedCount => Scoped.Count(r => r.Item.Status == ChangeStatus.Blocked);

        /// <summary>Items the Apply step would write in the selected tab.</summary>
        public int WillWriteCount => Scoped.Count(r => r.Item.WillWrite);

        /// <summary>Items in the selected tab still waiting for a decision (not applied until decided).</summary>
        public int PendingCount => Scoped.Count(r => r.Item.Decision == ReviewDecision.Pending);

        /// <summary>Items Apply would write across all schedules (Apply is never scoped).</summary>
        public int TotalWillWriteCount => _rows.Count(r => r.Item.WillWrite);

        public string ApplyText => ScheduleTabs.Count > 2
            ? $"Apply {TotalWillWriteCount} change(s) in all schedules ›"
            : $"Apply {TotalWillWriteCount} change(s) ›";

        public string VisibleText =>
            $"{Scoped.Count(r => _selectedFilter.Matches(r.Item)):N0} of {Scoped.Count():N0} shown";

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

        /// <summary>Rows in the selected schedule tab.</summary>
        private IEnumerable<ChangeRowViewModel> Scoped => _rows.Where(r => _selectedTab.Contains(r.Item));

        private void RaiseCountsChanged()
        {
            foreach (var name in new[] { nameof(ChangeCount), nameof(TypeChangeCount), nameof(ConflictCount), nameof(WarningCount),
                         nameof(BlockedCount), nameof(WillWriteCount), nameof(PendingCount), nameof(VisibleText) })
            {
                OnPropertyChanged(name);
            }
        }

        /// <summary>
        /// Asks for type-change approval and records the decision. Runs outside the ComboBox binding update,
        /// so failures are reported (and logged) instead of silently swallowed by WPF.
        /// </summary>
        internal void RequestTypeApproval(ChangeRowViewModel row, ReviewDecision decision)
        {
            try
            {
                if (ConfirmTypeChange(row.Item))
                {
                    row.SetDecision(decision);
                    DecisionsChanged();
                }
            }
            catch (Exception ex)
            {
                _reportError("The type change could not be approved.", ex);
            }
            finally
            {
                row.RefreshSelection();
            }
        }

        internal void DecisionsChanged()
        {
            OnPropertyChanged(nameof(WillWriteCount));
            OnPropertyChanged(nameof(PendingCount));
            OnPropertyChanged(nameof(TotalWillWriteCount));
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
            foreach (var row in Scoped.Where(r => r.Item.Status == status))
            {
                row.SetDecision(decision);
            }

            DecisionsChanged();
        }

        private void SkipAll()
        {
            foreach (var row in Scoped.Where(r => r.Item.Status is ChangeStatus.Change or ChangeStatus.TypeChange))
            {
                row.SetDecision(ReviewDecision.Skip);
            }

            DecisionsChanged();
        }

        private void ApproveAllTypeChanges()
        {
            var pending = Scoped.Where(r => r.Item.Status == ChangeStatus.TypeChange && r.Item.Decision != ReviewDecision.Apply).ToList();
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
            foreach (var row in Scoped.Where(r => r.Item.ConflictKind == ConflictKind.ThreeWay))
            {
                row.SetDecision(ReviewDecision.KeepRevit);
            }

            DecisionsChanged();
        }
    }

    /// <summary>A schedule tab above the review grid. SheetName null = all schedules.</summary>
    public sealed class ScheduleTab
    {
        private ScheduleTab(string? sheetName, string label)
        {
            SheetName = sheetName;
            Label = label;
        }

        public string? SheetName { get; }

        /// <summary>Name plus the number of changes and problems (unchanged values are not counted).</summary>
        public string Label { get; }

        public bool Contains(ChangeItem item) => SheetName == null || item.SheetName == SheetName;

        public override string ToString() => Label;

        /// <summary>"All schedules" first, then schedules in workbook order.</summary>
        public static IReadOnlyList<ScheduleTab> Create(ChangeSet changes)
        {
            static int Relevant(IEnumerable<ChangeItem> items) => items.Count(i => i.Status != ChangeStatus.NoChange);

            var tabs = new List<ScheduleTab> { new(null, $"All schedules ({Relevant(changes.Items)})") };
            foreach (var sheet in changes.Workbook.Sheets)
            {
                var items = changes.Items.Where(i => i.SheetName == sheet.SheetName).ToList();
                if (items.Count > 0)
                {
                    tabs.Add(new ScheduleTab(sheet.SheetName, $"{sheet.ScheduleName} ({Relevant(items)})"));
                }
            }

            return tabs;
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
                // The dialog is shown after the ComboBox has finished committing its selection: a modal dialog
                // inside a binding update is fragile, and WPF silently swallows exceptions thrown here.
                bool writesType = Item.IsType && value.Decision is ReviewDecision.Apply or ReviewDecision.UseExcel;
                if (writesType)
                {
                    var requested = value.Decision;
                    Dispatcher.CurrentDispatcher.BeginInvoke(() => _owner.RequestTypeApproval(this, requested));
                    return;
                }

                SetDecision(value.Decision);
                _owner.DecisionsChanged();
            }
        }

        /// <summary>Makes the ComboBox show the real decision again (e.g. after a cancelled approval).</summary>
        internal void RefreshSelection() => OnPropertyChanged(nameof(SelectedAction));

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
            ChangeStatus.Create => "New element",
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
            ChangeStatus.Create => [new("Create", ReviewDecision.Apply), new("Skip", ReviewDecision.Skip)],
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
            new("Changes", i => i.Status is ChangeStatus.Change or ChangeStatus.TypeChange or ChangeStatus.Create),
            new("New elements", i => i.Status == ChangeStatus.Create),
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
