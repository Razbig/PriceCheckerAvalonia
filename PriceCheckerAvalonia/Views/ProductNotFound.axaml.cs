using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace PriceCheckerAvalonia.Views;

public partial class ProductNotFound : UserControl
{
    public ProductNotFound(
        string title = "Товар не знайдено",
        string message = "Спробуйте ще раз або зверніться до співробітника")
    {
        InitializeComponent();
        ErrorTitle.Text = title;
        ErrorMessage.Text = message;
    }
    private void CloseAssistant_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow mainWindow)
        {
            mainWindow.CloseErrorPopup();
        }
    }

}