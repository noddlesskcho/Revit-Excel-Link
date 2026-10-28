using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Input;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Models;
using RevitScheduleSync.Services;

namespace RevitScheduleSync.UI.ViewModels
{
    /// <summary>Paths chosen by the user for model-data exports and profiles (null when cancelled).</summary>
    public sealed record ModelDataDialogs(
        Func<string, string?> ChooseExportPath,
        Func<string?> ChooseProfileSavePath,
        Func<string?> ChooseProfileOpenPath);

    /// <summary>
    /// One model-data tab (Model Categories, Annotation Categories, Elements, Spatial, Project Standards):
    /// export to Excel. Re-import uses the Import tab.
    /// </summary>
    public sealed class ModelDataViewModel : SectionViewModel
    {
        private ViewModelBase _currentStep;

        /// <param name="sources">Sources offered on this tab; a picker is shown when there is more than one.</param>
        /// <param name="goToImport">Switches the window to the Import tab.</param>
        public ModelDataViewModel(
            string title, IReadOnlyList<ModelSourceKind> sources, IScheduleOperations operations, ModelDataDialogs dialogs,
            ILogger logger, Action<string, Exception> reportError, Action goToImport)
            : base(title)
        {
            Selection = new ModelSelectionViewModel(sources, operations, dialogs, logger, reportError, ShowResult, goToImport);
            _currentStep = Selection;
        }

        public ModelSelectionViewModel Selection { get; }

        public ViewModelBase CurrentStep
        {
            get => _currentStep;
            private set => SetProperty(ref _currentStep, value);
        }

        private void ShowResult(ExportResult result, Action<string, Exception> reportError) =>
            CurrentStep = new ExportResultViewModel(result, back: () => CurrentStep = Selection, startOver: () => CurrentStep = Selection, reportError);
    }

    public sealed record SourceOption(ModelSourceKind Kind, string Name, string Description)
    {
        public override string ToString() => Name;
    }

    public sealed class CheckItem<T> : ViewModelBase
    {
        private readonly Action _changed;
        private bool _isChecked;

        public CheckItem(T value, string text, Action changed)
        {
            Value = value;
            Text = text;
            _changed = changed;
        }

        public T Value { get; }

        public string Text { get; }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (SetProperty(ref _isChecked, value))
                {
                    _changed();
                }
            }
        }
    }

    /// <summary>A parameter in the available or selected list.</summary>
    public sealed class ParameterItem : ViewModelBase
    {
        private bool _isSelected;

        public ParameterItem(ParameterChoice choice)
        {
            Choice = choice;
        }

        public ParameterChoice Choice { get; }

        public string Name => Choice.Name;

        public string ScopeText => Choice.IsReadOnly ? "Read-only" : Choice.IsType ? "Type" : "Instance";

        public string Detail => $"{Choice.Parameter.SourceText} · {Choice.GroupName}";

        /// <summary>List-box selection (several items can be moved at once).</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }

    public sealed class ModelSelectionViewModel : ViewModelBase
    {
        private static readonly IReadOnlyList<SourceOption> AllSources =
        [
            new(ModelSourceKind.ModelCategories, "Model categories", "Walls, doors, equipment ... one sheet per category."),
            new(ModelSourceKind.AnnotationCategories, "Annotation categories", "Tags, text, dimensions ... one sheet per category."),
            new(ModelSourceKind.Selection, "Current selection", "Elements selected in Revit when the window opened."),
            new(ModelSourceKind.Rooms, "Rooms", "Edit rooms, or add rows in Excel to create new unplaced rooms."),
            new(ModelSourceKind.Spaces, "Spaces", "Edit MEP spaces, or add rows in Excel to create new unplaced spaces."),
            new(ModelSourceKind.ProjectInformation, "Project information", "Project name, number, client, address ..."),
            new(ModelSourceKind.ObjectStyles, "Object styles", "Line weights, colours, patterns and materials of categories."),
            new(ModelSourceKind.LineStyles, "Line styles", "Line weight, colour and pattern of each line style."),
            new(ModelSourceKind.Families, "Families", "List of families and types with instance counts (information only).")
        ];

        private readonly IScheduleOperations _operations;
        private readonly ModelDataDialogs _dialogs;
        private readonly ILogger _logger;
        private readonly Action<string, Exception> _reportError;
        private readonly Action<ExportResult, Action<string, Exception>> _showResult;
        private readonly List<ParameterItem> _available = [];
        private SourceOption _selectedSource;
        private bool _useActiveView;
        private bool _includeLinked;
        private bool _exportByType;
        private bool _emptyTemplate;
        private bool _showInstance = true;
        private bool _showType = true;
        private bool _showReadOnly = true;
        private string _categorySearch = string.Empty;
        private string _parameterSearch = string.Empty;
        private bool _suspendRefresh;

        public ModelSelectionViewModel(
            IReadOnlyList<ModelSourceKind> sources, IScheduleOperations operations, ModelDataDialogs dialogs, ILogger logger,
            Action<string, Exception> reportError, Action<ExportResult, Action<string, Exception>> showResult, Action goToImport)
        {
            _operations = operations;
            _dialogs = dialogs;
            _logger = logger;
            _reportError = reportError;
            _showResult = showResult;
            Sources = AllSources.Where(s => sources.Contains(s.Kind)).ToList();
            _selectedSource = Sources[0];

            CategoriesView = CollectionViewSource.GetDefaultView(Categories);
            CategoriesView.Filter = o => o is CheckItem<CategoryChoice> c && Matches(c.Text, _categorySearch);
            AvailableView = CollectionViewSource.GetDefaultView(_available);
            AvailableView.Filter = o => o is ParameterItem p && IsAvailableVisible(p);

            SelectAllCategoriesCommand = new RelayCommand(() => SetCategories(true), ex => reportError("Could not select categories.", ex));
            ClearCategoriesCommand = new RelayCommand(() => SetCategories(false), ex => reportError("Could not clear categories.", ex));
            AddCommand = new RelayCommand(AddParameters, ex => reportError("Could not add parameters.", ex));
            AddAllCommand = new RelayCommand(AddAllVisible, ex => reportError("Could not add parameters.", ex));
            RemoveCommand = new RelayCommand(RemoveParameters, ex => reportError("Could not remove parameters.", ex));
            RemoveAllCommand = new RelayCommand(() => { Selected.Clear(); RefreshParameterViews(); }, ex => reportError("Could not clear parameters.", ex));
            MoveUpCommand = new RelayCommand(() => Move(-1), ex => reportError("Could not reorder parameters.", ex));
            MoveDownCommand = new RelayCommand(() => Move(1), ex => reportError("Could not reorder parameters.", ex));
            SaveProfileCommand = new RelayCommand(SaveProfile, ex => reportError("The profile could not be saved.", ex));
            LoadProfileCommand = new RelayCommand(LoadProfile, ex => reportError("The profile could not be loaded.", ex));
            ExportCommand = new RelayCommand(Export, ex => reportError("Export failed.", ex), () => CanExport);
            ImportCommand = new RelayCommand(goToImport, ex => reportError("Could not open Import.", ex));
            ResetCommand = new RelayCommand(Reset, ex => reportError("Could not reset.", ex));

            ReloadSource();
        }

        public IReadOnlyList<SourceOption> Sources { get; }

        public bool HasSourceChoice => Sources.Count > 1;

        public SourceOption SelectedSource
        {
            get => _selectedSource;
            set
            {
                if (value != null && SetProperty(ref _selectedSource, value))
                {
                    ReloadSource();
                }
            }
        }

        public ObservableCollection<CheckItem<CategoryChoice>> Categories { get; } = [];

        public ICollectionView CategoriesView { get; }

        public ICollectionView AvailableView { get; }

        public ObservableCollection<ParameterItem> Selected { get; } = [];

        // Which options apply to the current source.
        public bool ShowsCategories => Kind is ModelSourceKind.ModelCategories or ModelSourceKind.AnnotationCategories or ModelSourceKind.Selection;
        public bool ShowsParameters => Kind is not (ModelSourceKind.ObjectStyles or ModelSourceKind.LineStyles or ModelSourceKind.Families);
        public bool ShowsScope => Kind is ModelSourceKind.ModelCategories or ModelSourceKind.AnnotationCategories or ModelSourceKind.Rooms or ModelSourceKind.Spaces;
        public bool ShowsLinked => Kind is ModelSourceKind.ModelCategories or ModelSourceKind.AnnotationCategories or ModelSourceKind.Rooms or ModelSourceKind.Spaces;
        public bool ShowsByType => Kind is ModelSourceKind.ModelCategories or ModelSourceKind.AnnotationCategories or ModelSourceKind.Selection;
        public bool ShowsTemplate => Kind is ModelSourceKind.Rooms or ModelSourceKind.Spaces;
        public bool HasActiveView => _operations.HasActiveView;

        public string SourceDescription => _selectedSource.Description;

        public bool UseActiveView
        {
            get => _useActiveView;
            set
            {
                if (SetProperty(ref _useActiveView, value))
                {
                    ReloadSource();
                }
            }
        }

        public bool IncludeLinked
        {
            get => _includeLinked;
            set => SetProperty(ref _includeLinked, value);
        }

        public bool ExportByType
        {
            get => _exportByType;
            set
            {
                if (SetProperty(ref _exportByType, value))
                {
                    RefreshParameters();
                }
            }
        }

        public bool EmptyTemplate
        {
            get => _emptyTemplate;
            set => SetProperty(ref _emptyTemplate, value);
        }

        public bool ShowInstance
        {
            get => _showInstance;
            set { if (SetProperty(ref _showInstance, value)) RefreshParameterViews(); }
        }

        public bool ShowType
        {
            get => _showType;
            set { if (SetProperty(ref _showType, value)) RefreshParameterViews(); }
        }

        public bool ShowReadOnly
        {
            get => _showReadOnly;
            set { if (SetProperty(ref _showReadOnly, value)) RefreshParameterViews(); }
        }

        public string CategorySearch
        {
            get => _categorySearch;
            set { if (SetProperty(ref _categorySearch, value ?? string.Empty)) CategoriesView.Refresh(); }
        }

        public string ParameterSearch
        {
            get => _parameterSearch;
            set { if (SetProperty(ref _parameterSearch, value ?? string.Empty)) RefreshParameterViews(); }
        }

        public string CategorySummary => Kind == ModelSourceKind.Selection && _operations.SelectionCount == 0
            ? "Nothing is selected in Revit. Select elements, then open Revit-Excel Link again."
            : $"{Categories.Count(c => c.IsChecked)} of {Categories.Count} categories selected · {CheckedElementCount:N0} elements";

        public string ParameterSummary => $"{_available.Count} parameters found · {Selected.Count} selected";

        public bool CanExport => Kind switch
        {
            ModelSourceKind.ObjectStyles or ModelSourceKind.LineStyles or ModelSourceKind.Families => true,
            ModelSourceKind.Rooms or ModelSourceKind.Spaces or ModelSourceKind.ProjectInformation => true,
            _ => Categories.Any(c => c.IsChecked)
        };

        public ICommand SelectAllCategoriesCommand { get; }
        public ICommand ClearCategoriesCommand { get; }
        public ICommand AddCommand { get; }
        public ICommand AddAllCommand { get; }
        public ICommand RemoveCommand { get; }
        public ICommand RemoveAllCommand { get; }
        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }
        public ICommand SaveProfileCommand { get; }
        public ICommand LoadProfileCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand ResetCommand { get; }

        private ModelSourceKind Kind => _selectedSource.Kind;

        private ModelScope Scope => _useActiveView && HasActiveView ? ModelScope.ActiveView : ModelScope.WholeModel;

        private int CheckedElementCount => Categories.Where(c => c.IsChecked).Sum(c => c.Value.Count);

        private void ReloadSource()
        {
            _suspendRefresh = true;
            try
            {
                Categories.Clear();
                if (ShowsCategories)
                {
                    foreach (var category in WithWaitCursor(() => _operations.GetCategories(Kind, Scope)))
                    {
                        Categories.Add(new CheckItem<CategoryChoice>(category, $"{category.Name}  ({category.Count:N0})", OnCategoriesChanged));
                    }

                    // The selection is usually small and deliberate: export all of it by default.
                    if (Kind == ModelSourceKind.Selection)
                    {
                        foreach (var item in Categories)
                        {
                            item.IsChecked = true;
                        }
                    }
                }
            }
            finally
            {
                _suspendRefresh = false;
            }

            Selected.Clear();
            RefreshParameters();
            OnPropertyChanged(string.Empty);
        }

        private void OnCategoriesChanged()
        {
            if (!_suspendRefresh)
            {
                RefreshParameters();
            }

            OnPropertyChanged(nameof(CategorySummary));
            OnPropertyChanged(nameof(CanExport));
        }

        private void RefreshParameters()
        {
            _available.Clear();
            if (ShowsParameters)
            {
                var ids = Categories.Where(c => c.IsChecked).Select(c => c.Value.CategoryId).ToList();
                if (!ShowsCategories || ids.Count > 0)
                {
                    var choices = WithWaitCursor(() => _operations.GetParameters(Kind, Scope, ids, _exportByType && ShowsByType));
                    _available.AddRange(choices.Select(c => new ParameterItem(c)));
                }
            }

            // Keep selected parameters that still exist, in their order.
            var keys = _available.Select(p => p.Choice.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var item in Selected.Where(s => !keys.Contains(s.Choice.Key)).ToList())
            {
                Selected.Remove(item);
            }

            RefreshParameterViews();
        }

        private void RefreshParameterViews()
        {
            AvailableView.Refresh();
            OnPropertyChanged(nameof(ParameterSummary));
        }

        private bool IsAvailableVisible(ParameterItem item)
        {
            if (Selected.Any(s => s.Choice.Key == item.Choice.Key) || !Matches(item.Name, _parameterSearch))
            {
                return false;
            }

            return item.Choice.IsReadOnly ? _showReadOnly : item.Choice.IsType ? _showType : _showInstance;
        }

        private void AddParameters()
        {
            foreach (var item in _available.Where(p => p.IsSelected && IsAvailableVisible(p)).ToList())
            {
                item.IsSelected = false;
                Selected.Add(item);
            }

            RefreshParameterViews();
        }

        private void AddAllVisible()
        {
            foreach (var item in _available.Where(IsAvailableVisible).ToList())
            {
                item.IsSelected = false;
                Selected.Add(item);
            }

            RefreshParameterViews();
        }

        private void RemoveParameters()
        {
            foreach (var item in Selected.Where(p => p.IsSelected).ToList())
            {
                item.IsSelected = false;
                Selected.Remove(item);
            }

            RefreshParameterViews();
        }

        private void Move(int direction)
        {
            var moving = Selected.Where(p => p.IsSelected).ToList();
            var ordered = direction < 0 ? moving : Enumerable.Reverse(moving).ToList();
            foreach (var item in ordered)
            {
                int index = Selected.IndexOf(item);
                int target = index + direction;
                if (target < 0 || target >= Selected.Count || Selected[target].IsSelected)
                {
                    continue;
                }

                Selected.Move(index, target);
            }
        }

        /// <summary>Clears categories, selected parameters, searches and options on this tab.</summary>
        private void Reset()
        {
            _categorySearch = string.Empty;
            _parameterSearch = string.Empty;
            _includeLinked = false;
            _exportByType = false;
            _emptyTemplate = false;
            _showInstance = _showType = _showReadOnly = true;
            ReloadSource();
            CategoriesView.Refresh();
        }

        private void SetCategories(bool value)
        {
            _suspendRefresh = true;
            try
            {
                foreach (var item in Categories.Where(c => CategoriesView.Filter(c)))
                {
                    item.IsChecked = value;
                }
            }
            finally
            {
                _suspendRefresh = false;
            }

            OnCategoriesChanged();
        }

        private ModelExportRequest BuildRequest() => new()
        {
            Source = Kind,
            Scope = Scope,
            CategoryIds = Categories.Where(c => c.IsChecked).Select(c => c.Value.CategoryId).ToList(),
            ParameterKeys = Selected.Select(p => p.Choice.Key).ToList(),
            IncludeLinkedModels = ShowsLinked && _includeLinked,
            ExportByType = ShowsByType && _exportByType,
            EmptyTemplate = ShowsTemplate && _emptyTemplate
        };

        private void Export()
        {
            var path = _dialogs.ChooseExportPath(_selectedSource.Name);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var request = BuildRequest();
            var result = WithWaitCursor(() => _operations.ExportModel(request, new ExportOptions { FilePath = path }));
            _showResult(result, _reportError);
        }

        private void SaveProfile()
        {
            var path = _dialogs.ChooseProfileSavePath();
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var profile = new ModelExportProfile
            {
                Name = Path.GetFileNameWithoutExtension(path),
                Source = Kind,
                Scope = Scope,
                Categories = Categories.Where(c => c.IsChecked).Select(c => c.Value.Name).ToList(),
                ParameterKeys = Selected.Select(p => p.Choice.Key).ToList(),
                IncludeLinkedModels = _includeLinked,
                ExportByType = _exportByType
            };

            File.WriteAllText(path, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
            _logger.Info($"Profile saved: '{Path.GetFileName(path)}'.");
        }

        private void LoadProfile()
        {
            var path = _dialogs.ChooseProfileOpenPath();
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var profile = JsonSerializer.Deserialize<ModelExportProfile>(File.ReadAllText(path))
                          ?? throw new ScheduleSyncException("The profile file is empty.");

            _exportByType = profile.ExportByType;
            _includeLinked = profile.IncludeLinkedModels;
            _useActiveView = profile.Scope == ModelScope.ActiveView;
            _selectedSource = Sources.FirstOrDefault(s => s.Kind == profile.Source)
                              ?? throw new ScheduleSyncException(
                                  $"This profile is for \"{AllSources.First(s => s.Kind == profile.Source).Name}\". Load it on that tab.");
            ReloadSource();

            // Categories by name: ids of non-built-in categories differ between projects.
            var names = profile.Categories.ToHashSet(StringComparer.OrdinalIgnoreCase);
            _suspendRefresh = true;
            try
            {
                foreach (var item in Categories)
                {
                    item.IsChecked = names.Contains(item.Value.Name);
                }
            }
            finally
            {
                _suspendRefresh = false;
            }

            RefreshParameters();
            var byKey = _available.ToDictionary(p => p.Choice.Key, StringComparer.Ordinal);
            int missing = 0;
            foreach (var key in profile.ParameterKeys)
            {
                if (byKey.TryGetValue(key, out var item))
                {
                    Selected.Add(item);
                }
                else
                {
                    missing++;
                }
            }

            RefreshParameterViews();
            OnPropertyChanged(string.Empty);
            _logger.Info($"Profile loaded: '{Path.GetFileName(path)}', {missing} parameter(s) not found in this project.");
        }

        private static bool Matches(string text, string search) =>
            search.Length == 0 || text.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase);

        private static T WithWaitCursor<T>(Func<T> work)
        {
            var previous = Mouse.OverrideCursor;
            Mouse.OverrideCursor = Cursors.Wait;
            try
            {
                return work();
            }
            finally
            {
                Mouse.OverrideCursor = previous;
            }
        }
    }
}
