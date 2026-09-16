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
        // Try to find product in local DB
        try
        {
            var app = Avalonia.Application.Current as App;
            var localDb = app?.LocalDb; // core LocalDatabase
            PriceCheckerAvalonia.Core.Model.Product? product = null;
            if (localDb != null)
            {
                product = localDb.FindByBarcode(barcode);
            }

            if (product != null)
            {
                // Show product info
                var info = new ProductInfo();
                info.SetProduct(product);
                ProductInfoFrame.Content = info;
                ShowFrame(ProductInfoFrame);
                // If image path is a URL (starts with http), download in background and update DB/UI
                if (!string.IsNullOrWhiteSpace(product.ImagePath) &&
                    (product.ImagePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || product.ImagePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var app = Avalonia.Application.Current as App;
                            var http = new System.Net.Http.HttpClient();
                            var imgUrl = product.ImagePath!;
                            var imgBytes = await http.GetByteArrayAsync(imgUrl);
                            var imagesDir = System.IO.Path.Combine(Environment.CurrentDirectory, "images");
                            System.IO.Directory.CreateDirectory(imagesDir);
                            var ext = System.IO.Path.GetExtension(new Uri(imgUrl).AbsolutePath);
                            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
                            var fileName = product.Barcode + ext;
                            var savePath = System.IO.Path.Combine(imagesDir, fileName);
                            await System.IO.File.WriteAllBytesAsync(savePath, imgBytes);

                            // Update DB record
                            app?.LocalDb?.UpsertProducts(new[] { new PriceCheckerAvalonia.Core.Model.Product
                            {
                                Barcode = product.Barcode,
                                ImagePath = savePath,
                                Id = product.Id,
                                Article = product.Article,
                                Name = product.Name,
                                Price = product.Price,
                                Category = product.Category,
                                Country = product.Country,
                                Brand = product.Brand,
                                ProductType = product.ProductType,
                                StockQty = product.StockQty,
                                UpdatedAt = DateTime.UtcNow
                            }});

                            // Refresh UI on main thread
                            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                            {
                                // Ensure the file is flushed by the background task; small retry loop
                                var imagesDir = System.IO.Path.Combine(Environment.CurrentDirectory, "images");
                                var ext = System.IO.Path.GetExtension(new Uri(imgUrl).AbsolutePath);
                                if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
                                var fileName = product.Barcode + ext;
                                var savePath = System.IO.Path.Combine(imagesDir, fileName);

                                // Try to load the file a few times if it's still being written
                                for (int i = 0; i < 5; i++)
                                {
                                    if (System.IO.File.Exists(savePath)) break;
                                    await Task.Delay(100);
                                }

                                // If current frame holds ProductInfo, update its image directly
                                if (ProductInfoFrame.Content is PriceCheckerAvalonia.Views.ProductInfo pi)
                                {
                                    pi.SetImageFromFile(savePath);
                                }

                                // Also refresh product data in DB and update text fields if needed
                                var updated = app?.LocalDb?.FindByBarcode(product.Barcode);
                                if (updated != null && ProductInfoFrame.Content is PriceCheckerAvalonia.Views.ProductInfo pi2)
                                {
                                    pi2.SetProduct(updated);
                                }
                            });
                        }
                        catch
                        {
                            // ignore background download errors
                        }
                    });
                }
            }
            else
            {
                // Not found
                ShowErrorPopup();
            }
        }
        catch
        {
            ShowErrorPopup();
        }
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
        ShowFrame(SettingsFrame);
        SettingsFrame.Content = new Settings();
    }

    // ──────────────────────────────────────────────
    // Анімації (FadeIn / FadeOut)
    // ──────────────────────────────────────────────

    private void ShowFrame(Control frameToShow)
    {
        Control[] allFrames = { MainFrame, ProductNotFoundFrame, AssistantLoginFrame, ProductInfoFrame, SettingsFrame};

        foreach (var frame in allFrames)
        {
            if (frame == frameToShow)
            {
                frame.IsVisible = true;
                //_ = AnimateFadeIn(frame);
            }
            else
            {
                frame.IsVisible = false;
                //_ = AnimateFadeOut(frame);
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