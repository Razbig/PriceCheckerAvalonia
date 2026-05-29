using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using PriceCheckerAvalonia.Views;
using System;
using System.Text;
using System.Threading.Tasks;

namespace PriceCheckerAvalonia;

public partial class MainWindow : Window
{
    // === Сканер ===
    private readonly StringBuilder _barcodeBuffer = new StringBuilder();
    private DateTime _lastKeyPress = DateTime.MinValue;
    private const int BarcodeTimeoutMs = 70;

    public MainWindow()
    {
        InitializeComponent();

        // PreviewKeyDown → Tunnel KeyDown в Avalonia
        this.AddHandler(
            InputElement.KeyDownEvent,
            MainWindow_PreviewKeyDown,
            RoutingStrategies.Tunnel);

        this.Opened += MainWindow_Opened;
        MainFrame.Content = new MainPage();
    }

    private void MainWindow_Opened(object? sender, EventArgs e)
    {
        this.Focus();
    }

    // ──────────────────────────────────────────────
    // Сканер
    // ──────────────────────────────────────────────

    private void MainWindow_PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var now = DateTime.Now;
        _lastKeyPress = now;

        // Enter — штрихкод готовий
        if (e.Key == Key.Enter)
        {
            if (_barcodeBuffer.Length > 4)
            {
                string barcode = _barcodeBuffer.ToString().Trim();
                _barcodeBuffer.Clear();
                e.Handled = true;
                ProcessBarcode(barcode);
                return;
            }
            else
            {
                _barcodeBuffer.Clear();
            }
        }

        char? ch = GetCharFromKey(e.Key);
        if (ch.HasValue)
            _barcodeBuffer.Append(ch.Value);
    }

    private char? GetCharFromKey(Key key)
    {
        if (key >= Key.D0 && key <= Key.D9)
            return (char)('0' + (key - Key.D0));

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
            return (char)('0' + (key - Key.NumPad0));

        if (key >= Key.A && key <= Key.Z)
            return char.ToUpper(key.ToString()[0]);

        // Key.Return не існує в Avalonia — тільки Key.Enter
        if (key == Key.OemMinus || key == Key.Subtract) return '-';
        if (key == Key.OemPeriod || key == Key.Decimal) return '.';
        if (key == Key.OemPlus || key == Key.Add) return '+';

        return null;
    }

    private void ProcessBarcode(string barcode)
    {
        if (ProductNotFoundFrame.IsVisible)
        {
            HideBlurDialog();
            SetMainFrameVisible(true);
        }

        //ShowFrame(ProductInfoFrame);
        //ShowFrame(ProductNotFoundFrame);
        ShowErrorPopup();
    }

    // ──────────────────────────────────────────────
    // Публічні методи
    // ──────────────────────────────────────────────

    public void ShowErrorPopup()
    {
        TakeBlurSnapshot();
        BlurOverlay.IsVisible = true;
        ProductNotFoundFrame.Content = new ProductNotFound();
        ProductNotFoundFrame.IsVisible = true;
    }

    public void HideBlurDialog()
    {
        BlurOverlay.IsVisible = false;
        BlurredSnapshot.Source = null;
    }

    public void SetMainFrameVisible(bool visible)
    {
        MainFrame.IsVisible = visible;
    }

    public void NavigateToPage(Control page)
    {
        MainFrame.Content = page;
        MainFrame.IsVisible = true;
    }

    public void NavigateFrame(ContentControl targetFrame, Control page)
    {
        if (targetFrame == null) return;
        targetFrame.Content = page;
        targetFrame.IsVisible = true;
    }

    // ──────────────────────────────────────────────
    // Обробники кліків
    // ──────────────────────────────────────────────

    private void OpenAssistant_Click(object? sender, RoutedEventArgs e)
    {
        TakeBlurSnapshot();
        ShowFrame(AssistantLoginFrame);
        AssistantLoginFrame.Content = new AssistantLogin();

        CloseAssistantButton.IsVisible = true;
        OpenAssistantButton.IsVisible = false;
        SettingsButton.IsVisible = true;
    }

    private void CloseAssistantLogin_Click(object? sender, RoutedEventArgs e)
    {
        HideBlurDialog();
        AssistantLoginFrame.IsVisible = false;
        AssistantLoginFrame.Content = null;

        ShowFrame(MainFrame);

        CloseAssistantButton.IsVisible = false;
        OpenAssistantButton.IsVisible = true;
        SettingsButton.IsVisible = false;
    }

    private void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        // TODO
    }

    // ──────────────────────────────────────────────
    // Анімації (FadeIn / FadeOut)
    // ──────────────────────────────────────────────

    private void ShowFrame(Control frameToShow)
    {
        Control[] allFrames = { MainFrame, ProductNotFoundFrame, AssistantLoginFrame, ProductInfoFrame };

        foreach (var frame in allFrames)
        {
            if (frame == frameToShow)
            {
                frame.IsVisible = true;
                //_ = AnimateFadeIn(frame);
            }
            else
            {
                _ = AnimateFadeOut(frame);
            }
        }
    }

    private async Task AnimateFadeIn(Control element)
    {
        element.Opacity = 0;
        element.IsVisible = true;

        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(500),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(OpacityProperty, 0d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(OpacityProperty, 1d) }
                }
            }
        };

        await animation.RunAsync(element);
    }

    private async Task AnimateFadeOut(Control element)
    {
        if (!element.IsVisible) return;

        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(200),
            Easing = new CubicEaseIn(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters = { new Setter(OpacityProperty, 1d) }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters = { new Setter(OpacityProperty, 0d) }
                }
            }
        };

        await animation.RunAsync(element);
        element.IsVisible = false;
        element.Opacity = 1; // скидаємо для наступного разу
    }

    // ──────────────────────────────────────────────
    // Приватні допоміжні методи
    // ──────────────────────────────────────────────

    private void TakeBlurSnapshot()
    {
        double width = MainRootGrid.Bounds.Width;
        double height = MainRootGrid.Bounds.Height;

        if (width > 0 && height > 0)
        {
            var bmp = new RenderTargetBitmap(
                new PixelSize((int)width, (int)height));
            bmp.Render(MainRootGrid);
            BlurredSnapshot.Source = bmp;
        }
    }
}