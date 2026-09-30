using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Models;

namespace RevitScheduleSync.Revit.Schedules
{
    /// <summary>
    /// Reads the ViewSchedule elements of a document into plain ScheduleSummary objects.
    /// Read-only: never opens a transaction. Must run in Revit API context.
    /// </summary>
    internal sealed class ScheduleCatalog
    {
        private const string ExcludedTemplates = "Schedule templates";
        private const string ExcludedRevision = "Title block revision schedules";
        private const string ExcludedKeynote = "Internal keynote schedules";
        private const string ExcludedOtherViewType = "Other system schedules";

        private readonly Document _document;
        private readonly ILogger _logger;

        public ScheduleCatalog(Document document, ILogger logger)
        {
            _document = document;
            _logger = logger;
        }

        public ScheduleScanResult Scan()
        {
            var stopwatch = Stopwatch.StartNew();
            var excluded = new Dictionary<string, int>();
            var summaries = new List<ScheduleSummary>();

            // Panel schedules (PanelScheduleView) and graphical column schedules are not
            // ViewSchedule instances, so OfClass(ViewSchedule) already leaves them out.
            var schedules = new FilteredElementCollector(_document)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>();

            foreach (var schedule in schedules)
            {
                var exclusion = GetExclusionReason(schedule);
                if (exclusion != null)
                {
                    excluded[exclusion] = excluded.TryGetValue(exclusion, out var n) ? n + 1 : 1;
                    continue;
                }

                try
                {
                    summaries.Add(Summarise(schedule));
                }
                catch (Exception ex)
                {
                    // One odd schedule must not hide all the others.
                    _logger.Warning($"Could not read schedule (ElementId {schedule.Id.Value}).", ex);
                    excluded["Unreadable schedules"] = excluded.TryGetValue("Unreadable schedules", out var n) ? n + 1 : 1;
                }
            }

            summaries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            stopwatch.Stop();

            var excludedText = excluded.Count == 0
                ? "none excluded"
                : "excluded: " + string.Join(", ", excluded.Select(e => $"{e.Key} {e.Value}"));
            _logger.Info($"Schedule scan: {summaries.Count} listed, {excludedText}, {stopwatch.ElapsedMilliseconds} ms.");

            return new ScheduleScanResult
            {
                Schedules = summaries,
                ExcludedCounts = excluded,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
            };
        }

        private static string? GetExclusionReason(ViewSchedule schedule)
        {
            if (schedule.IsTemplate)
            {
                return ExcludedTemplates;
            }

            if (schedule.IsTitleblockRevisionSchedule)
            {
                return ExcludedRevision;
            }

            // Internal schedules Revit creates for keynote filtering; users cannot see or edit them.
            if (schedule.IsInternalKeynoteSchedule)
            {
                return ExcludedKeynote;
            }

            if (schedule.ViewType != ViewType.Schedule)
            {
                return ExcludedOtherViewType;
            }

            return null;
        }

        private ScheduleSummary Summarise(ViewSchedule schedule)
        {
            var definition = schedule.Definition;
            var kind = GetKind(definition);

            int fieldCount = definition.GetFieldCount();
            int hiddenCount = 0;
            for (int i = 0; i < fieldCount; i++)
            {
                if (definition.GetField(i).IsHidden)
                {
                    hiddenCount++;
                }
            }

            var (support, note) = GetSupport(definition, kind);

            return new ScheduleSummary
            {
                UniqueId = schedule.UniqueId,
                ElementIdValue = schedule.Id.Value,
                Name = schedule.Name,
                CategoryName = GetCategoryName(definition.CategoryId),
                Kind = kind,
                IsItemized = definition.IsItemized,
                FieldCount = fieldCount,
                HiddenFieldCount = hiddenCount,
                EstimatedElementCount = support == ScheduleSupport.Unsupported ? null : CountElements(schedule),
                IncludesLinkedFiles = definition.IncludeLinkedFiles,
                IsFilteredBySheet = definition.IsFilteredBySheet,
                Support = support,
                SupportNote = note
            };
        }

        private static ScheduleKind GetKind(ScheduleDefinition definition)
        {
            if (definition.IsKeySchedule)
            {
                return ScheduleKind.KeySchedule;
            }

            if (definition.IsMaterialTakeoff)
            {
                return ScheduleKind.MaterialTakeoff;
            }

            // Multi-category schedules report InvalidElementId as their category.
            var categoryId = definition.CategoryId;
            if (categoryId == ElementId.InvalidElementId)
            {
                return ScheduleKind.MultiCategory;
            }

            return categoryId.Value switch
            {
                (long)BuiltInCategory.OST_Sheets => ScheduleKind.SheetList,
                (long)BuiltInCategory.OST_Views => ScheduleKind.ViewList,
                // Generic Annotation schedules are created as note blocks (ViewSchedule.CreateNoteBlock).
                (long)BuiltInCategory.OST_GenericAnnotation => ScheduleKind.NoteBlock,
                _ => ScheduleKind.Regular
            };
        }

        private static (ScheduleSupport Support, string Note) GetSupport(ScheduleDefinition definition, ScheduleKind kind)
        {
            switch (kind)
            {
                case ScheduleKind.KeySchedule:
                    return (ScheduleSupport.Unsupported, "Key schedules are not supported in V1.");
                case ScheduleKind.MaterialTakeoff:
                    return (ScheduleSupport.Unsupported, "Material takeoffs are not supported in V1.");
            }

            // Embedded schedules interleave rows of another category, so rows no longer map 1:1 to elements.
            if (definition.HasEmbeddedSchedule || definition.IsEmbedded)
            {
                return (ScheduleSupport.Unsupported, "Schedules with an embedded schedule are not supported in V1.");
            }

            return definition.IsItemized
                ? (ScheduleSupport.EditableSync, "Itemised — editable sync supported.")
                : (ScheduleSupport.PresentationOnly, "Grouped — presentation export only, re-import not supported.");
        }

        private string GetCategoryName(ElementId categoryId)
        {
            if (categoryId == ElementId.InvalidElementId)
            {
                return "Multiple Categories";
            }

            return Category.GetCategory(_document, categoryId)?.Name ?? "Unknown";
        }

        /// <summary>
        /// Estimate only: a view collector on a schedule returns the elements the schedule
        /// currently lists (after its filters and phase). Returns null if Revit refuses.
        /// </summary>
        private int? CountElements(ViewSchedule schedule)
        {
            try
            {
                return new FilteredElementCollector(_document, schedule.Id).GetElementCount();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Could not count elements of schedule ElementId {schedule.Id.Value}.", ex);
                return null;
            }
        }
    }
}
