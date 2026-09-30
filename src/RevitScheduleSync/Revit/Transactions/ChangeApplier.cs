using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Models;
using RevitScheduleSync.Models.Import;
using RevitScheduleSync.Revit.Parameters;
using RevitScheduleSync.Sync;

namespace RevitScheduleSync.Revit.Transactions
{
    /// <summary>
    /// Writes approved changes (ChangeItem.WillWrite) to the model.
    ///
    /// Transaction strategy:
    /// - One Transaction for the whole import, so the user gets a single Undo step.
    /// - One SubTransaction per value: a value that fails to set is rolled back on its own and reported,
    ///   the rest continue.
    /// - A failures preprocessor records Revit warnings (e.g. duplicate Mark) and removes them from the
    ///   warning dialog. Errors make Revit roll back the transaction; the elements Revit names are then
    ///   excluded and the transaction is tried once more, so one bad element cannot block all others.
    /// </summary>
    internal sealed class ChangeApplier
    {
        private readonly Document _document;
        private readonly ILogger _logger;
        private readonly ParameterLocator _parameters;
        private string _undoName = "Schedule Sync import";

        public ChangeApplier(Document document, ILogger logger)
        {
            _document = document;
            _logger = logger;
            _parameters = new ParameterLocator(document);
        }

        public ImportReport Apply(ChangeSet changes)
        {
            var stopwatch = Stopwatch.StartNew();
            if (changes.IsBlocked)
            {
                throw new InvalidOperationException("Import is blocked: the workbook belongs to another project.");
            }

            // Shown in Revit's Undo list.
            _undoName = $"Schedule Sync import ({Path.GetFileName(changes.Workbook.FilePath)})";
            var toWrite = changes.Items.Where(i => i.WillWrite).ToList();
            _logger.Info($"Apply started: {toWrite.Count} value(s) to write.");

            var failed = new Dictionary<ChangeItem, string>();
            var warnings = new List<string>();
            AttemptResult attempt = Attempt(toWrite, failed, warnings);

            if (attempt.RolledBack && attempt.FailingIds.Count > 0)
            {
                // Retry once without the elements Revit reported as failing.
                var excluded = toWrite.Where(i => attempt.FailingIds.Contains(TargetId(i))).ToList();
                foreach (var item in excluded)
                {
                    failed[item] = $"Revit rejected the change: {attempt.ErrorText}";
                }

                var retry = toWrite.Except(excluded).ToList();
                _logger.Warning($"Apply rolled back ({attempt.ErrorText}). Retrying without {excluded.Count} value(s).");
                warnings.Clear();
                attempt = retry.Count > 0 ? Attempt(retry, failed, warnings) : new AttemptResult(true, [], attempt.ErrorText, []);
            }

            var applied = attempt.RolledBack ? new HashSet<ChangeItem>() : attempt.Written.ToHashSet();
            if (attempt.RolledBack)
            {
                foreach (var item in toWrite.Where(i => !failed.ContainsKey(i)))
                {
                    failed[item] = $"Not applied: Revit rolled back the import ({attempt.ErrorText}).";
                }
            }

            var entries = changes.Items
                .Where(i => i.Status != ChangeStatus.NoChange)
                .Select(i => ToEntry(i, applied, failed))
                .ToList();

            stopwatch.Stop();
            var report = new ImportReport
            {
                Time = DateTime.Now,
                WorkbookPath = changes.Workbook.FilePath,
                DocumentTitle = _document.Title,
                Entries = entries,
                RevitWarnings = warnings.Distinct().ToList(),
                RolledBack = attempt.RolledBack && applied.Count == 0 && toWrite.Count > 0,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
            };

            _logger.Info($"Apply finished in {stopwatch.ElapsedMilliseconds} ms: applied {report.Count(ReportResult.Applied)}, " +
                         $"failed {report.Count(ReportResult.Failed)}, skipped {report.Count(ReportResult.Skipped)}, " +
                         $"conflicts {report.Count(ReportResult.Conflict)}, read-only {report.Count(ReportResult.ReadOnlyIgnored)}, " +
                         $"Revit warnings {report.RevitWarnings.Count}, rolled back {attempt.RolledBack}.");
            return report;
        }

        private sealed record AttemptResult(bool RolledBack, List<ChangeItem> Written, string ErrorText, HashSet<long> FailingIds);

        private AttemptResult Attempt(List<ChangeItem> items, Dictionary<ChangeItem, string> failed, List<string> warnings)
        {
            var written = new List<ChangeItem>();
            var preprocessor = new ImportFailuresPreprocessor();

            using var transaction = new Transaction(_document, _undoName);
            var options = transaction.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(preprocessor);
            options.SetClearAfterRollback(true);
            transaction.SetFailureHandlingOptions(options);

            transaction.Start();
            foreach (var item in items)
            {
                if (failed.ContainsKey(item))
                {
                    continue;
                }

                using var sub = new SubTransaction(_document);
                sub.Start();
                try
                {
                    var error = Write(item);
                    if (error == null)
                    {
                        sub.Commit();
                        written.Add(item);
                    }
                    else
                    {
                        sub.RollBack();
                        failed[item] = error;
                    }
                }
                catch (Exception ex)
                {
                    if (sub.HasStarted() && !sub.HasEnded())
                    {
                        sub.RollBack();
                    }

                    failed[item] = $"Failed: {ex.Message}";
                    _logger.Warning($"Write failed for element {item.ElementId}, '{item.ParameterName}'.", ex);
                }
            }

            var status = transaction.Commit();
            warnings.AddRange(preprocessor.Warnings);

            bool rolledBack = status != TransactionStatus.Committed;
            if (rolledBack)
            {
                _logger.Warning($"Transaction not committed ({status}): {string.Join(" | ", preprocessor.Errors)}");
            }

            return new AttemptResult(
                rolledBack,
                written,
                preprocessor.Errors.Count > 0 ? string.Join("; ", preprocessor.Errors.Distinct()) : status.ToString(),
                preprocessor.FailingElementIds);
        }

        /// <summary>
        /// Re-resolves and re-checks one value, then sets it. Returns null on success, else the reason.
        /// </summary>
        private string? Write(ChangeItem item)
        {
            var target = item.IsType
                ? (item.TypeUniqueId == null ? null : _document.GetElement(item.TypeUniqueId))
                : _document.GetElement(item.ElementUniqueId);
            if (target == null)
            {
                return "Element no longer exists.";
            }

            var parameter = item.Parameter == null ? null : _parameters.Find(target, item.Parameter);
            if (parameter == null)
            {
                return "Parameter not found.";
            }

            if (parameter.IsReadOnly)
            {
                return "Parameter is read-only.";
            }

            // The model should not have changed since analysis (the window is modal), but never overwrite blindly.
            if (item.CurrentNormalised != null)
            {
                var now = ParameterValueReader.Read(parameter, item.Kind, item.ExportUnitTypeId, null).Normalised;
                if (!ValueComparer.AreEqual(item.Kind, now, item.CurrentNormalised))
                {
                    return "Value changed in Revit after analysis. Re-analyse the workbook.";
                }
            }

            var value = item.Target!;
            bool ok = value.Storage switch
            {
                ValueStorage.String when parameter.StorageType == StorageType.String => parameter.Set(value.Text ?? string.Empty),
                ValueStorage.Integer when parameter.StorageType == StorageType.Integer => parameter.Set(value.Integer!.Value),
                ValueStorage.Double when parameter.StorageType == StorageType.Double => parameter.Set(value.Double!.Value),
                ValueStorage.ElementId when parameter.StorageType == StorageType.ElementId => parameter.Set(new ElementId(value.ElementIdValue!.Value)),
                _ => false
            };

            return ok ? null : "Revit did not accept the value.";
        }

        private long TargetId(ChangeItem item)
        {
            if (item.IsType && item.TypeUniqueId != null)
            {
                return _document.GetElement(item.TypeUniqueId)?.Id.Value ?? -1;
            }

            return item.ElementId;
        }

        private static ImportReportEntry ToEntry(ChangeItem item, HashSet<ChangeItem> applied, Dictionary<ChangeItem, string> failed)
        {
            ReportResult result;
            string message;

            if (applied.Contains(item))
            {
                (result, message) = (ReportResult.Applied, item.Status == ChangeStatus.Conflict ? "Conflict resolved: Excel value used." : "Applied.");
            }
            else if (failed.TryGetValue(item, out var error))
            {
                (result, message) = (ReportResult.Failed, error);
            }
            else
            {
                (result, message) = item.Status switch
                {
                    ChangeStatus.Change or ChangeStatus.TypeChange => (ReportResult.Skipped,
                        item.Decision == ReviewDecision.Pending ? "Skipped: no decision was made." : "Skipped by reviewer."),
                    ChangeStatus.Conflict => (ReportResult.Conflict,
                        item.Decision == ReviewDecision.KeepRevit ? "Conflict: Revit value kept." : $"Conflict not applied. {item.Message}"),
                    ChangeStatus.ReadOnly => (ReportResult.ReadOnlyIgnored, item.Message),
                    ChangeStatus.RevitChanged or ChangeStatus.SameChange or ChangeStatus.Duplicate => (ReportResult.Skipped, item.Message),
                    _ => (ReportResult.NotApplicable, item.Message)
                };
            }

            return new ImportReportEntry(result, item.ScheduleName, item.ElementId, item.ElementLabel, item.ParameterName,
                item.IsType, item.Current, item.Excel, message);
        }
    }

    /// <summary>Collects warnings (and removes them from the UI) and errors (which roll the transaction back).</summary>
    internal sealed class ImportFailuresPreprocessor : IFailuresPreprocessor
    {
        public List<string> Warnings { get; } = [];
        public List<string> Errors { get; } = [];
        public HashSet<long> FailingElementIds { get; } = [];

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            bool hasErrors = false;
            foreach (var failure in failuresAccessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                {
                    Warnings.Add(failure.GetDescriptionText());
                    failuresAccessor.DeleteWarning(failure);
                }
                else
                {
                    hasErrors = true;
                    Errors.Add(failure.GetDescriptionText());
                    foreach (var id in failure.GetFailingElementIds())
                    {
                        FailingElementIds.Add(id.Value);
                    }
                }
            }

            return hasErrors ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }
}
