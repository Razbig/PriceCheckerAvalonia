using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using System;

namespace PriceCheckerAvalonia.Views;

public partial class AssistantLogin : UserControl
{
    public event Action<string>? PinSubmitted;

    public AssistantLogin()
    {
        InitializeComponent();
    }

    public string PinCode => PinCodeTextBox.Text?.Trim() ?? string.Empty;

    public void ClearPinCode()
    {
        PinCodeTextBox.Clear();
    }

    private void SubmitPin()
    {
        var pinCode = PinCode;
        if (!string.IsNullOrWhiteSpace(pinCode))
            PinSubmitted?.Invoke(pinCode);
    }

    private void PinButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string digit)
            PinCodeTextBox.Text += digit;
    }

    private void ClearPinButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ClearPinCode();
    }

    private void BackspacePinButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var text = PinCodeTextBox.Text ?? string.Empty;
        if (text.Length > 0)
            PinCodeTextBox.Text = text[..^1];
    }

    private void ConfirmPinButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SubmitPin();
    }

    private void PinCodeTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SubmitPin();
            e.Handled = true;
        }
    }
}