using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Input;
using RevitScheduleSync.Models;

namespace RevitScheduleSync.UI.ViewModels
{
    /// <summary>Export step 2: per-schedule field analysis, read-only review.</summary>
    public sealed class ScheduleAnalysisViewModel : ViewModelBase
    {
        private AnalysedScheduleViewModel? _selectedSchedule;

        private bool _protectReadOnlyCells = true;
        private bool _showMetadataSheet;

        public ScheduleAnalysisViewModel(
            IReadOnlyList<ScheduleAnalysis> analyses,
            Action back,
            Action<ScheduleAnalysisViewModel> export,
            Action<string, Exception> reportError)
        {
            Schedules = analyses.Select(a => new AnalysedScheduleViewModel(a)).ToList();
            _selectedSchedule = Schedules.FirstOrDefault();

            BackCommand = new RelayCommand(back, ex => reportError("Could not return to schedule selection.", ex));
            ExportCommand = new RelayCommand(
                () => export(this),
                ex => reportError("The export could not be completed.", ex),
                () => Schedules.Any(s => !s.HasError));

            int editable = analyses.Sum(a => a.EditableInstanceCount + a.EditableTypeCount);
            int failed = analyses.Count(a => a.Error != null);
            SummaryText = $"{analyses.Count} schedule(s) analysed · {editable} editable column(s)" +
                          (failed > 0 ? $" · {failed} could not be analysed" : string.Empty);
        }

        public IReadOnlyList<AnalysedScheduleViewModel> Schedules { get; }

        public AnalysedScheduleViewModel? SelectedSchedule
        {
            get => _selectedSchedule;
            set => SetProperty(ref _selectedSchedule, value);
        }

        public string SummaryText { get; }

        /// <summary>Lock read-only cells (sheet protection without password). Blocks sorting in Excel.</summary>
        public bool ProtectReadOnlyCells
        {
            get => _protectReadOnlyCells;
            set => SetProperty(ref _protectReadOnlyCells, value);
        }

        /// <summary>Debug option: leave the metadata sheet visible.</summary>
        public bool ShowMetadataSheet
        {
            get => _showMetadataSheet;
            set => SetProperty(ref _showMetadataSheet, value);
        }

        public ICommand BackCommand { get; }

        public ICommand ExportCommand { get; }
    }

    /// <summary>One analysed schedule in the left-hand list.</summary>
    public sealed class AnalysedScheduleViewModel
    {
        public AnalysedScheduleViewModel(ScheduleAnalysis analysis)
        {
            Analysis = analysis;
            Fields = analysis.Fields.Select(f => new FieldRowViewModel(f)).ToList();
        }

        public ScheduleAnalysis Analysis { get; }

        public IReadOnlyList<FieldRowViewModel> Fields { get; }

        public string Name => Analysis.Schedule.Name;

        public bool HasError => Analysis.Error != null;

        public string? Error => Analysis.Error;

        public ScheduleSupport Support => Analysis.Schedule.Support;

        public string LayoutText => Analysis.Schedule.Support == ScheduleSupport.PresentationOnly
            ? "GROUPED — initially restricted"
            : "ITEMISED — editable sync supported";

        public string CountsText => HasError
            ? "Not analysed"
            : $"{Analysis.EditableInstanceCount} instance · {Analysis.EditableTypeCount} type · " +
              $"{Analysis.ReadOnlyCount} read-only · {Analysis.UnsupportedCount} unsupported";

        public string SampleText => $"Writability checked on {Analysis.SampledElementCount} sampled element(s) " +
                                    $"of ~{Analysis.Schedule.EstimatedElementCount?.ToString("N0") ?? "?"}.";

        public IReadOnlyList<string> Notes => Analysis.Notes;

        public bool HasNotes => Analysis.Notes.Count > 0;
    }

    /// <summary>One schedule field in the analysis grid.</summary>
    public sealed class FieldRowViewModel
    {
        public FieldRowViewModel(ScheduleFieldInfo field)
        {
            Field = field;
        }

        public ScheduleFieldInfo Field { get; }

        public int Index => Field.FieldIndex + 1;

        public string Heading => Field.Heading;

        public string ParameterName => Field.Parameter.Source == ParameterSource.None ? Field.FieldName : Field.Parameter.Name;

        public string SourceText => Field.Parameter.SourceText;

        public string ScopeText => Field.Parameter.Source == ParameterSource.None
            ? "–"
            : Field.IsRelatedElement ? "Related"
            : Field.IsTypeParameter ? "Type" : "Instance";

        public string StorageText => Field.Storage == ValueStorage.None ? "–" : Field.Storage.ToString();

        public string ValueTypeText
        {
            get
            {
                if (Field.IsYesNo)
                {
                    return "Yes/No";
                }

                if (Field.IsMeasurable)
                {
                    return string.IsNullOrEmpty(Field.DisplayUnitLabel)
                        ? Field.SpecLabel
                        : $"{Field.SpecLabel} ({Field.DisplayUnitLabel})";
                }

                return string.IsNullOrEmpty(Field.SpecLabel) ? "–" : Field.SpecLabel;
            }
        }

        public FieldClassification Classification => Field.Classification;

        public string ClassificationText => Field.Classification switch
        {
            FieldClassification.EditableInstance => "Editable",
            FieldClassification.EditableType => "Editable (type)",
            FieldClassification.ReadOnlyCalculated => "Calculated",
            FieldClassification.ReadOnlySystem => "Read-only",
            FieldClassification.ReadOnlyRelated => "Related element",
            _ => "Unsupported"
        };

        public string FlagsText
        {
            get
            {
                var flags = new List<string>();
                if (Field.IsHidden)
                {
                    flags.Add("Hidden");
                }

                if (Field.Parameter.Source != ParameterSource.None
                    && !string.Equals(Field.Heading, Field.FieldName, StringComparison.Ordinal))
                {
                    flags.Add("Renamed heading");
                }

                if (Field.RequiresReferenceMapping && Field.IsEditable)
                {
                    flags.Add("Reference");
                }

                if (Field.HasCustomFormat)
                {
                    flags.Add("Custom units");
                }

                if (Field.DisplayType != "Standard")
                {
                    flags.Add(Field.DisplayType);
                }

                return string.Join(" · ", flags);
            }
        }

        public string Reason => Field.Reason;

        /// <summary>Technical identity details, for diagnosing mapping problems.</summary>
        public string DetailText
        {
            get
            {
                var p = Field.Parameter;
                var text = new StringBuilder()
                    .AppendLine(Field.Reason)
                    .AppendLine()
                    .AppendLine($"Field id: {Field.FieldId}   Field type: {Field.FieldType}")
                    .AppendLine($"Revit field name: {Field.FieldName}")
                    .AppendLine($"Parameter key: {p.Key}");

                if (p.BuiltInName != null)
                {
                    text.AppendLine($"BuiltInParameter: {p.BuiltInName} ({p.BuiltInValue})");
                }

                if (p.SharedGuid.HasValue)
                {
                    text.AppendLine($"Shared GUID: {p.SharedGuid:D}");
                }

                if (p.ParameterElementId.HasValue)
                {
                    text.AppendLine($"ParameterElement id: {p.ParameterElementId}");
                }

                if (!string.IsNullOrEmpty(Field.SpecTypeId))
                {
                    text.AppendLine($"Spec: {Field.SpecTypeId}");
                }

                if (!string.IsNullOrEmpty(Field.DisplayUnitTypeId))
                {
                    text.AppendLine($"Display unit: {Field.DisplayUnitTypeId}" +
                                    (Field.DisplayAccuracy.HasValue
                                        ? $"   accuracy {Field.DisplayAccuracy.Value.ToString(CultureInfo.InvariantCulture)}"
                                        : string.Empty));
                }

                if (Field.SampleFound > 0)
                {
                    text.AppendLine($"Sample: found on {Field.SampleFound}, writable on {Field.SampleWritable}");
                }

                return text.ToString().TrimEnd();
            }
        }
    }
}
