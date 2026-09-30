using System;
using System.Windows.Input;

namespace RevitScheduleSync.UI.ViewModels
{
    /// <summary>
    /// ICommand that routes any exception to an error handler instead of letting it escape
    /// into the WPF dispatcher (which, inside Revit, is Revit's own dispatcher).
    /// </summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;
        private readonly Action<Exception> _onError;

        public RelayCommand(Action execute, Action<Exception> onError, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _onError = onError;
            _canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object? parameter)
        {
            try
            {
                _execute();
            }
            catch (Exception ex)
            {
                _onError(ex);
            }
        }
    }
}
