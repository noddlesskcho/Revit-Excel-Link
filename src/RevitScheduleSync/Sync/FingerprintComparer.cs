using System;
using System.Collections.Generic;
using RevitScheduleSync.Models;
using RevitScheduleSync.Models.Import;

namespace RevitScheduleSync.Sync
{
    /// <summary>
    /// Decides whether a workbook belongs to the open project. Any hard identifier that differs
    /// blocks the import; titles and file names are never enough on their own.
    /// Note: "Save As" copies and detached copies keep the same identifiers and count as the same project.
    /// </summary>
    public static class FingerprintComparer
    {
        public static FingerprintResult Compare(DocumentFingerprint workbook, DocumentFingerprint current)
        {
            var mismatches = new List<string>();
            var notes = new List<string>();
            int compared = 0;

            Check("document creation id", workbook.CreationGuid, current.CreationGuid);
            Check("project information id", workbook.ProjectInfoUniqueId, current.ProjectInfoUniqueId);
            Check("central model id", workbook.CentralGuid, current.CentralGuid);
            Check("cloud project", workbook.CloudProjectGuid, current.CloudProjectGuid);
            Check("cloud model", workbook.CloudModelGuid, current.CloudModelGuid);

            if (!string.IsNullOrEmpty(workbook.ProjectNumber) && !string.IsNullOrEmpty(current.ProjectNumber)
                && !string.Equals(workbook.ProjectNumber, current.ProjectNumber, StringComparison.Ordinal))
            {
                notes.Add($"Project number differs: workbook \"{workbook.ProjectNumber}\", model \"{current.ProjectNumber}\".");
            }

            if (!string.Equals(workbook.Title, current.Title, StringComparison.OrdinalIgnoreCase))
            {
                notes.Add($"Exported from \"{workbook.Title}\"; the open model is \"{current.Title}\".");
            }

            if (workbook.CentralModelName != null && current.CentralModelName != null
                && !string.Equals(workbook.CentralModelName, current.CentralModelName, StringComparison.OrdinalIgnoreCase))
            {
                notes.Add($"Central model name differs: \"{workbook.CentralModelName}\" vs \"{current.CentralModelName}\".");
            }

            if (mismatches.Count > 0)
            {
                mismatches.Insert(0, "This workbook appears to belong to a different project. Import is blocked.");
                mismatches.AddRange(notes);
                return new FingerprintResult(FingerprintMatch.Mismatch, mismatches);
            }

            if (compared < 2)
            {
                notes.Insert(0, "Only some project identifiers could be compared. Check that this is the right project.");
                return new FingerprintResult(FingerprintMatch.Probable, notes);
            }

            return new FingerprintResult(FingerprintMatch.Match, notes);

            void Check(string label, string? expected, string? actual)
            {
                if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(actual))
                {
                    return;
                }

                compared++;
                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                {
                    mismatches.Add($"Different {label}.");
                }
            }
        }
    }
}
