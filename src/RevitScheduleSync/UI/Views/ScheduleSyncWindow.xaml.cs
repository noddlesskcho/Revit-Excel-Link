using System;
using System.Windows;
using RevitScheduleSync.UI.ViewModels;

namespace RevitScheduleSync.UI.Views
{
    public partial class ScheduleSyncWindow : Window
    {
        public ScheduleSyncWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.RequestClose += OnRequestClose;
            Closed += (_, _) => viewModel.RequestClose -= OnRequestClose;
        }

        private void OnRequestClose(object? sender, EventArgs e) => Close();
    }
}
