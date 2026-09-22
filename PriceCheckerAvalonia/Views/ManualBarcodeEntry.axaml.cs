using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using System;

namespace PriceCheckerAvalonia.Views;

public partial class ManualBarcodeEntry : UserControl
{
    public event Action<string>? BarcodeSubmitted;
    public event Action? Cancelled;

    public ManualBarcodeEntry()
    {
        InitializeComponent();
    }

    public void FocusInput()
    {
        if (!IsLoaded)
        {
            Loaded -= FocusInputOnLoaded;
            Loaded += FocusInputOnLoaded;
            return;
        }

        BarcodeInput?.Focus();
    }

    private void FocusInputOnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Loaded -= FocusInputOnLoaded;
        FocusInput();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private TextBox? BarcodeInput => this.FindControl<TextBox>("BarcodeTextBox");

    private void Digit_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var input = BarcodeInput;
        if (sender is Button { Tag: string digit } && input != null)
        {
            input.Text = (input.Text ?? string.Empty) + digit;
            FocusInput();
        }
    }

    private void Clear_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        BarcodeInput?.Clear();
        FocusInput();
    }

    private void Backspace_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var input = BarcodeInput;
        var text = input?.Text ?? string.Empty;
        if (text.Length > 0)
            input!.Text = text[..^1];
        FocusInput();
    }

    private void Submit_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Submit();

    private void Cancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Cancelled?.Invoke();

    private void BarcodeTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Submit();
            e.Handled = true;
        }
    }

    private void Submit()
    {
        var barcode = BarcodeInput?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(barcode))
            BarcodeSubmitted?.Invoke(barcode);
    }
}
