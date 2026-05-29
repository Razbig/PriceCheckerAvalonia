using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace PriceCheckerAvalonia.Views;

public partial class ProductNotFound : UserControl
{
    public ProductNotFound()
    {
        InitializeComponent();
    }
    private void CloseAssistant_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow mainWindow)
        {
            mainWindow.ProductNotFoundFrame.IsVisible = false;
            mainWindow.HideBlurDialog();
            mainWindow.SetMainFrameVisible(true);
        }
    }

}