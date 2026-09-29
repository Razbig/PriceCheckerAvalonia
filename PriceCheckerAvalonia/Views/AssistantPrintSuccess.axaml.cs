using Avalonia.Controls;
using Avalonia.Interactivity;

namespace PriceCheckerAvalonia.Views;

public partial class AssistantPrintSuccess : UserControl
{
    public AssistantPrintSuccess() : this(
        "Цінники успішно відправлено на друк.",
        "Після завершення друку, вийдіть з кабінету асистента")
    {
    }

    public AssistantPrintSuccess(string title, string message)
    {
        InitializeComponent();
        SuccessTitle.Text = title;
        SuccessMessage.Text = message;
    }

    private void CloseAssistant_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow mainWindow)
        {
            mainWindow.CloseErrorPopup();
        }
    }

    private void ExitButton_Click(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow mainWindow)
        {
            mainWindow.CloseAssistant();
        }
    }
}