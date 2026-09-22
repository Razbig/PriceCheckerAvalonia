using Avalonia.Controls;
using PriceCheckerAvalonia.ViewModels;

namespace PriceCheckerAvalonia.Views
{
    public partial class AssistantPrint : UserControl
    {
        public AssistantPrint()
        {
            InitializeComponent();
            DataContext = new AssistantPrintViewModel();
        }
    }
}