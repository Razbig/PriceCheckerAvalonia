using System;
using System.Windows.Input;

namespace PriceCheckerAvalonia.Helpers
{
    /// <summary>
    /// Простий ICommand без залежностей від MVVM-фреймворків.
    /// Якщо в проєкті вже є CommunityToolkit.Mvvm або ReactiveUI — краще використати їх.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;

        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object? parameter) => _execute();

        public void RaiseCanExecuteChanged()
            => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        public event EventHandler? CanExecuteChanged;
    }
}