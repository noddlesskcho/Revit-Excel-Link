namespace RevitScheduleSync.UI.ViewModels
{
    /// <summary>One top-level area of the window (Export / Import / History).</summary>
    public abstract class SectionViewModel : ViewModelBase
    {
        protected SectionViewModel(string title)
        {
            Title = title;
        }

        public string Title { get; }
    }
}
