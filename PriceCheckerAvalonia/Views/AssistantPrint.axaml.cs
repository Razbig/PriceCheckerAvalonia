using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using PriceCheckerAvalonia.ViewModels;

namespace PriceCheckerAvalonia.Views
{
    public partial class AssistantPrint : UserControl
    {
        private AssistantPrintViewModel? _vm;

        public AssistantPrint()
        {
            InitializeComponent();
            DataContext = new AssistantPrintViewModel();

            // subscribe to PrintStatus changes
            this.AttachedToVisualTree += (s, e) =>
            {
                _vm = DataContext as AssistantPrintViewModel;
                if (_vm != null)
                    _vm.PropertyChanged += Vm_PropertyChanged;
            };

            this.DetachedFromVisualTree += (s, e) =>
            {
                if (_vm != null)
                    _vm.PropertyChanged -= Vm_PropertyChanged;
            };
        }

        private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "PrintStatus") return;
            if (DataContext is AssistantPrintViewModel vm)
            {
                var status = vm.PrintStatus ?? string.Empty;
                // exact match used in ViewModel on success
                if (status == "Цінники успішно відправлено на друк.")
                {
                    if (TopLevel.GetTopLevel(this) is MainWindow mainWindow)
                    {
                        // show success popup
                        mainWindow.ShowPrintSuccess();
                    }
                }
            }
        }
    }
}