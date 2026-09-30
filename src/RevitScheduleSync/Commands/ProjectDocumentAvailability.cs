using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitScheduleSync.Commands
{
    /// <summary>
    /// Enables the ribbon button only when a project (not family) document is active.
    /// Called frequently by Revit: must be cheap and must not throw.
    /// </summary>
    public sealed class ProjectDocumentAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        {
            try
            {
                var document = applicationData.ActiveUIDocument?.Document;
                return document != null && !document.IsFamilyDocument;
            }
            catch
            {
                return false;
            }
        }
    }
}
