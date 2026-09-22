using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using PriceCheckerAvalonia.ViewModels;
using PriceCheckerAvalonia.Views;
using PriceCheckerAvalonia.Services;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PriceCheckerAvalonia;

public partial class MainWindow : Window
{
    // === Сканер ===
    private readonly StringBuilder _barcodeBuffer = new StringBuilder();
    private DateTime _lastKeyPress = DateTime.MinValue;
    private const int BarcodeTimeoutMs = 70;

    private const int InactivitySeconds = 30;
    private const int LeftMouseButton = 0x01;
    private static readonly string VideoFolder = GetVideoFolder();
    private readonly DispatcherTimer _inactivityTimer;
    private readonly DispatcherTimer _videoMouseTimer;
    private readonly List<string> _videoFiles = new();
    private readonly MediaContentService _mediaContentService;
    private readonly CancellationTokenSource _mediaUpdateCancellation = new();
    private LibVLC? _libVlc;
    private MediaPlayer? _mediaPlayer;
    private Media? _currentMedia;
    private int _currentVideoIndex;
    private ContentControl? _frameBeforeError;
    private ContentControl? _frameBeforeManualEntry;
    private IntPtr _xDisplay;
    private UIntPtr _xRootWindow;
    private bool _linuxMouseDetectionUnavailableLogged;
    private readonly bool[] _linuxKeyStates = new bool[256];

    public MainWindow()
    {
        InitializeComponent();
        ManualBarcodeButton.Click += ManualBarcode_Click;

        // PreviewKeyDown → Tunnel KeyDown в Avalonia
        this.AddHandler(
            InputElement.KeyDownEvent,
            MainWindow_PreviewKeyDown,
            RoutingStrategies.Tunnel);

        this.Opened += MainWindow_Opened;
        this.Closed += MainWindow_Closed;
        MainFrame.Content = new MainPage();
        _mediaContentService = new MediaContentService(VideoFolder);

        _inactivityTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(InactivitySeconds)
        };
        _inactivityTimer.Tick += InactivityTimer_Tick;
        _inactivityTimer.Start();

        _videoMouseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _videoMouseTimer.Tick += VideoMouseTimer_Tick;

        AddHandler(InputElement.PointerPressedEvent,
            MainWindow_PointerPressed,
            RoutingStrategies.Tunnel);
    }

    private void MainWindow_Opened(object? sender, EventArgs e)
    {
        this.Focus();
        _mediaContentService.WriteDiagnostic($"Main window opened. Media directory: {VideoFolder}.");
        _ = UpdateMediaContentAsync();
    }

    private async Task UpdateMediaContentAsync()
    {
        try
        {
            var files = await _mediaContentService
                .UpdateAsync(_mediaUpdateCancellation.Token)
                .ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _videoFiles.Clear();
                _videoFiles.AddRange(files);
                _mediaContentService.WriteDiagnostic(
                    $"Main window playlist refreshed: {files.Count} file(s): {string.Join("; ", files)}");

                if (_currentVideoIndex >= _videoFiles.Count)
                    _currentVideoIndex = 0;
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MediaContent] Update failed: {ex}");
        }
    }

    // ──────────────────────────────────────────────
    // Сканер
    // ──────────────────────────────────────────────

    private void MainWindow_PreviewKeyDown(object? sender, KeyEventArgs e)
    {
        ResetInactivityTimer();

        if (ManualBarcodeFrame.IsVisible)
            return;

        if (VideoOverlay.IsVisible)
        {
            if (OperatingSystem.IsLinux() && _xDisplay != IntPtr.Zero)
            {
                e.Handled = true;
                return;
            }

            HideVideo();
        }

        var now = DateTime.Now;
        _lastKeyPress = now;

        if (e.Key == Key.Enter && AssistantLoginFrame.Content is AssistantLogin assistantLogin)
        {
            var pinCode = assistantLogin.PinCode;
            if (!string.IsNullOrWhiteSpace(pinCode))
            {
                assistantLogin.ClearPinCode();
                _barcodeBuffer.Clear();
                e.Handled = true;
                ProcessBarcode(pinCode);
                return;
            }
        }

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

    private void MainWindow_PointerPressed(object? sender, PointerEventArgs e)
    {
        ResetInactivityTimer();
    }

    private void ResetInactivityTimer()
    {
        _inactivityTimer.Stop();
        _inactivityTimer.Start();
    }

    private void InactivityTimer_Tick(object? sender, EventArgs e)
    {
        _inactivityTimer.Stop();

        if (MainFrame.IsVisible && !AssistantLoginFrame.IsVisible &&
            !ProductInfoFrame.IsVisible && !ProductNotFoundFrame.IsVisible &&
            !SettingsFrame.IsVisible)
        {
            ShowNextVideo();
        }
        else
        {
            _inactivityTimer.Start();
        }

        if (!VideoOverlay.IsVisible)
            _inactivityTimer.Start();
    }

    private void ShowNextVideo()
    {
        if (!Directory.Exists(VideoFolder))
        {
            System.Diagnostics.Debug.WriteLine($"[Video] Directory does not exist: {VideoFolder}");
            _mediaContentService.WriteDiagnostic($"Playback skipped: directory does not exist: {VideoFolder}.");
            return;
        }

        if (_videoFiles.Count == 0)
        {
            _videoFiles.AddRange(Directory.EnumerateFiles(VideoFolder)
                .Where(IsVideoFile)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase));
        }

        if (_videoFiles.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine($"[Video] No video files found in: {VideoFolder}");
            _mediaContentService.WriteDiagnostic($"Playback skipped: no video files found in {VideoFolder}.");
            return;
        }

        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            _libVlc ??= new LibVLC();
            _mediaPlayer ??= new MediaPlayer(_libVlc);
            _mediaPlayer.EndReached -= MediaPlayer_EndReached;
            _mediaPlayer.EndReached += MediaPlayer_EndReached;
            VideoView.MediaPlayer = _mediaPlayer;

            _currentMedia?.Dispose();
            _currentMedia = new Media(_libVlc, _videoFiles[_currentVideoIndex], FromType.FromPath);
            _mediaContentService.WriteDiagnostic($"Starting playback: {_currentMedia.Mrl}.");
            _currentVideoIndex = (_currentVideoIndex + 1) % _videoFiles.Count;
            VideoOverlay.IsVisible = true;
            _videoMouseTimer.Start();
            var playbackStarted = _mediaPlayer.Play(_currentMedia);
            _mediaContentService.WriteDiagnostic($"LibVLC Play returned {playbackStarted}.");
            if (!playbackStarted)
            {
                System.Diagnostics.Debug.WriteLine($"[Video] LibVLC failed to play: {_currentMedia.Mrl}");
                HideVideo();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Video] Playback failed: {ex}");
            _mediaContentService.WriteDiagnostic($"Playback failed: {ex}");
            HideVideo();
        }
    }

    private static bool IsVideoFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".avi", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mov", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".wmv", StringComparison.OrdinalIgnoreCase);
    }

    private void MediaPlayer_EndReached(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(ShowNextVideo);
    }

    private void VideoOverlay_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        HideVideo();
        e.Handled = true;
    }

    private void VideoMouseTimer_Tick(object? sender, EventArgs e)
    {
        if (!VideoOverlay.IsVisible)
        {
            _videoMouseTimer.Stop();
            return;
        }

        if (OperatingSystem.IsWindows() &&
            (GetAsyncKeyState(LeftMouseButton) & 0x8000) != 0)
        {
            HideVideo();
            return;
        }

        if (OperatingSystem.IsLinux() && IsLinuxLeftMouseButtonDown())
        {
            HideVideo();
            return;
        }

        if (OperatingSystem.IsLinux())
            PollLinuxKeyboard();
    }

    private void PollLinuxKeyboard()
    {
        if (_xDisplay == IntPtr.Zero)
            return;

        var keyMap = new byte[32];
        if (XQueryKeymap(_xDisplay, keyMap) == IntPtr.Zero)
            return;

        for (var keyCode = 8; keyCode < 256; keyCode++)
        {
            var isDown = (keyMap[keyCode >> 3] & (1 << (keyCode & 7))) != 0;
            if (isDown && !_linuxKeyStates[keyCode])
                HandleLinuxKeyPress(keyCode);

            _linuxKeyStates[keyCode] = isDown;
            if (!VideoOverlay.IsVisible)
                return;
        }
    }

    private void HandleLinuxKeyPress(int keyCode)
    {
        var keySym = XkbKeycodeToKeysym(_xDisplay, (byte)keyCode, 0, 0).ToUInt64();
        if (keySym == 0xff0d || keySym == 0xff8d)
        {
            var barcode = _barcodeBuffer.ToString().Trim();
            _barcodeBuffer.Clear();
            if (barcode.Length > 4)
                ProcessBarcode(barcode);

            HideVideo();
            return;
        }

        if (keySym >= '0' && keySym <= '9')
        {
            _barcodeBuffer.Append((char)keySym);
        }
        else if (keySym >= 'a' && keySym <= 'z')
        {
            _barcodeBuffer.Append(char.ToUpperInvariant((char)keySym));
        }
        else if (keySym >= 'A' && keySym <= 'Z')
        {
            _barcodeBuffer.Append((char)keySym);
        }
        else if (keySym >= 0xffb0 && keySym <= 0xffb9)
        {
            _barcodeBuffer.Append((char)('0' + keySym - 0xffb0));
        }
    }

    private bool IsLinuxLeftMouseButtonDown()
    {
        try
        {
            if (_xDisplay == IntPtr.Zero)
            {
                _xDisplay = XOpenDisplay(IntPtr.Zero);
                if (_xDisplay == IntPtr.Zero)
                {
                    if (!_linuxMouseDetectionUnavailableLogged)
                    {
                        _mediaContentService.WriteDiagnostic(
                            "Linux mouse polling unavailable: XOpenDisplay returned null.");
                        _linuxMouseDetectionUnavailableLogged = true;
                    }

                    return false;
                }

                _xRootWindow = XRootWindow(_xDisplay, XDefaultScreen(_xDisplay));
            }

            var querySucceeded = XQueryPointer(
                _xDisplay,
                _xRootWindow,
                out _,
                out _,
                out _,
                out _,
                out _,
                out _,
                out var pointerMask);

            return querySucceeded != 0 && (pointerMask & (1u << 8)) != 0;
        }
        catch (Exception ex)
        {
            if (!_linuxMouseDetectionUnavailableLogged)
            {
                _mediaContentService.WriteDiagnostic($"Linux mouse polling failed: {ex}");
                _linuxMouseDetectionUnavailableLogged = true;
            }

            return false;
        }
    }

    private static string GetVideoFolder()
    {
        if (OperatingSystem.IsLinux())
            return "/mrraz/videos";

        var windowsFolder = @"C:\Users\mrraz\Downloads\videos";
        if (Directory.Exists(windowsFolder))
            return windowsFolder;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "videos");
    }

    private void HideVideo()
    {
        if (!VideoOverlay.IsVisible)
            return;

        _videoMouseTimer.Stop();
        VideoOverlay.IsVisible = false;
        ResetInactivityTimer();

        var mediaPlayer = _mediaPlayer;
        if (mediaPlayer != null)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    mediaPlayer.Stop();
                }
                catch
                {
                    // VLC may already be shutting down after the video surface is hidden.
                }
            });
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _inactivityTimer.Stop();
        _mediaPlayer?.Stop();
        _currentMedia?.Dispose();
        _mediaPlayer?.Dispose();
        _libVlc?.Dispose();
        if (_xDisplay != IntPtr.Zero)
        {
            XCloseDisplay(_xDisplay);
            _xDisplay = IntPtr.Zero;
        }
        _mediaUpdateCancellation.Cancel();
        _mediaUpdateCancellation.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport("libX11.so.6")]
    private static extern int XDefaultScreen(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern UIntPtr XRootWindow(IntPtr display, int screenNumber);

    [DllImport("libX11.so.6")]
    private static extern int XQueryPointer(
        IntPtr display,
        UIntPtr window,
        out UIntPtr rootWindow,
        out UIntPtr childWindow,
        out int rootX,
        out int rootY,
        out int windowX,
        out int windowY,
        out uint mask);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XQueryKeymap(IntPtr display, [Out] byte[] keysReturn);

    [DllImport("libX11.so.6")]
    private static extern UIntPtr XkbKeycodeToKeysym(IntPtr display, byte keyCode, int group, int level);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(IntPtr display);

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

    // ...existing code...
    private void ProcessBarcode(string barcode)
    {
        if (AssistantLoginFrame.Content is AssistantPrint assistantPrint &&
            assistantPrint.DataContext is AssistantPrintViewModel printViewModel)
        {
            var product = (Application.Current as App)?.LocalDb?.FindByBarcode(barcode);
            if (product != null)
            {
                printViewModel.AddProduct(product);
            }
            else
            {
                ShowErrorPopup();
            }

            return;
        }

        if (AssistantLoginFrame.IsVisible && barcode == "1234")
        {
            OpenAssistantPrint();
            return;
        }

        // Test shortcut: barcode 12345 opens AssistantPrint with test products.
        if (AssistantLoginFrame.IsVisible && barcode == "12345")
        {
            OpenAssistantPrint(addTestProducts: true);
            return;
        }

        if (AssistantLoginFrame.IsVisible)
        {
            ShowErrorPopup(
                "Помилка ідентифікації асистента",
                "Спробуйте ще раз");
            return;
        }

        var mainProduct = (Application.Current as App)?.LocalDb?.FindByBarcode(barcode);
        if (mainProduct != null)
        {
            var productInfo = new ProductInfo();
            productInfo.SetProduct(mainProduct);
            ProductInfoFrame.Content = productInfo;
            ShowFrame(ProductInfoFrame);
        }
        else
        {
            ShowErrorPopup();
        }

        return;
    }

    private void OpenAssistantPrint(bool addTestProducts = false)
    {
        var printViewModel = new AssistantPrintViewModel();
        if (addTestProducts)
            printViewModel.AddTestProducts();

        AssistantLoginFrame.Content = new AssistantPrint
        {
            DataContext = printViewModel
        };
    }

    // ──────────────────────────────────────────────
    // Публічні методи
    // ──────────────────────────────────────────────

    public void ShowErrorPopup(
        string title = "Товар не знайдено",
        string message = "Спробуйте ще раз або зверніться до співробітника")
    {
        if (!ProductNotFoundFrame.IsVisible)
        {
            _frameBeforeError = new[]
            {
                MainFrame,
                AssistantLoginFrame,
                ProductInfoFrame,
                SettingsFrame
            }.FirstOrDefault(frame => frame.IsVisible) ?? MainFrame;
        }

        TakeBlurSnapshot();
        ShowFrame(ProductNotFoundFrame);
        BlurOverlay.IsVisible = true;
        ProductNotFoundFrame.Content = new ProductNotFound(title, message);
    }

    public void CloseErrorPopup()
    {
        ProductNotFoundFrame.IsVisible = false;
        HideBlurDialog();
        ShowFrame(_frameBeforeError ?? MainFrame);
        _frameBeforeError = null;
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
        var assistantLogin = new AssistantLogin();
        assistantLogin.PinSubmitted += AssistantLogin_PinSubmitted;
        AssistantLoginFrame.Content = assistantLogin;

        CloseAssistantButton.IsVisible = true;
        OpenAssistantButton.IsVisible = false;
        SettingsButton.IsVisible = true;
    }

    private void AssistantLogin_PinSubmitted(string pinCode)
    {
        if (AssistantLoginFrame.Content is AssistantLogin assistantLogin)
            assistantLogin.ClearPinCode();

        _barcodeBuffer.Clear();
        ProcessBarcode(pinCode);
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
        ManualBarcodeButton.IsVisible = true;
    }

    private void ManualBarcode_Click(object? sender, RoutedEventArgs e)
    {
        if (VideoOverlay.IsVisible)
            HideVideo();

        _frameBeforeManualEntry = new[]
        {
            MainFrame,
            AssistantLoginFrame,
            ProductInfoFrame,
            SettingsFrame
        }.FirstOrDefault(frame => frame.IsVisible) ?? MainFrame;

        TakeBlurSnapshot();
        var entry = new ManualBarcodeEntry();
        entry.BarcodeSubmitted += barcode =>
        {
            CloseManualBarcodeEntry();
            _barcodeBuffer.Clear();
            ProcessBarcode(barcode);
        };
        entry.Cancelled += CloseManualBarcodeEntry;
        ManualBarcodeFrame.Content = entry;
        ShowFrame(ManualBarcodeFrame);
        BlurOverlay.IsVisible = true;
        entry.FocusInput();
    }

    private void CloseManualBarcodeEntry()
    {
        ManualBarcodeFrame.IsVisible = false;
        HideBlurDialog();
        ShowFrame(_frameBeforeManualEntry ?? MainFrame);
        _frameBeforeManualEntry = null;
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
        Control[] allFrames =
        {
            MainFrame,
            ProductNotFoundFrame,
            AssistantLoginFrame,
            ProductInfoFrame,
            SettingsFrame,
            ManualBarcodeFrame
        };

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