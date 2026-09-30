using System;
using System.IO;
using Autodesk.Revit.DB;
using RevitScheduleSync.Logging;
using RevitScheduleSync.Models;

namespace RevitScheduleSync.Revit.Documents
{
    /// <summary>Reads the identifiers that make up a DocumentFingerprint. Read-only.</summary>
    internal static class DocumentFingerprintReader
    {
        public static DocumentFingerprint Read(Document document, ILogger logger)
        {
            var projectInfo = document.ProjectInformation;
            string? centralName = null, centralGuid = null, cloudProject = null, cloudModel = null;

            if (document.IsWorkshared)
            {
                try
                {
                    var centralPath = document.GetWorksharingCentralModelPath();
                    if (centralPath != null)
                    {
                        // File name only: the folder can reveal server/share structure and is not needed.
                        var visible = ModelPathUtils.ConvertModelPathToUserVisiblePath(centralPath);
                        centralName = Path.GetFileName(visible.TrimEnd('/', '\\'));
                    }
                }
                catch (Exception ex)
                {
                    logger.Warning("Could not read the central model path.", ex);
                }

                try
                {
                    // Only Revit Server central models have this GUID; file-based centrals throw.
                    centralGuid = document.WorksharingCentralGUID.ToString("D");
                }
                catch (Exception)
                {
                    centralGuid = null;
                }
            }

            if (document.IsModelInCloud)
            {
                try
                {
                    var cloudPath = document.GetCloudModelPath();
                    cloudProject = cloudPath.GetProjectGUID().ToString("D");
                    cloudModel = cloudPath.GetModelGUID().ToString("D");
                }
                catch (Exception ex)
                {
                    logger.Warning("Could not read the cloud model identifiers.", ex);
                }
            }

            return new DocumentFingerprint
            {
                CreationGuid = document.CreationGUID.ToString("D"),
                ProjectInfoUniqueId = projectInfo?.UniqueId,
                ProjectNumber = projectInfo?.Number ?? string.Empty,
                ProjectName = projectInfo?.Name ?? string.Empty,
                Title = document.Title,
                IsWorkshared = document.IsWorkshared,
                CentralModelName = centralName,
                CentralGuid = centralGuid,
                CloudProjectGuid = cloudProject,
                CloudModelGuid = cloudModel
            };
        }
    }
}
