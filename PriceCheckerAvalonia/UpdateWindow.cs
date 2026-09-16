using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using PriceCheckerAvalonia.Core.Model;
using PriceCheckerAvalonia.Core.Services;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace PriceCheckerAvalonia
{
    public class UpdateWindow : Window
    {
        private readonly TextBlock _header;
        private readonly TextBlock _notes;
        private readonly ProgressBar _progress;
        private readonly Button _downloadButton;
        private readonly Button _laterButton;
        private readonly UpdateInfo _info;
        private readonly UpdateChecker _checker;

        public UpdateWindow(UpdateInfo info, UpdateChecker checker)
        {
            _info = info ?? throw new ArgumentNullException(nameof(info));
            _checker = checker ?? throw new ArgumentNullException(nameof(checker));

            Title = "Доступно обновление";
            Width = 700;
            Height = 500;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var panel = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
            _header = new TextBlock { FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold };
            _header.Text = $"Новая версия: {_info.Version}";
            panel.Children.Add(_header);

            _notes = new TextBlock();
            _notes.Text = _info.Notes ?? string.Empty;
            var sv = new ScrollViewer { Content = _notes, Height = 250 };
            panel.Children.Add(sv);

            _progress = new ProgressBar { Minimum = 0, Maximum = 1, Value = 0, Height = 18, IsVisible = false };
            panel.Children.Add(_progress);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            _laterButton = new Button { Content = "Позже" };
            _laterButton.Click += LaterButton_Click;
            buttons.Children.Add(_laterButton);
            _downloadButton = new Button { Content = "Скачать и установить" };
            _downloadButton.Click += DownloadButton_Click;
            buttons.Children.Add(_downloadButton);
            panel.Children.Add(buttons);

            Content = panel;
        }

        private void LaterButton_Click(object? sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void DownloadButton_Click(object? sender, RoutedEventArgs e)
        {
            _downloadButton.IsEnabled = false;
            _laterButton.IsEnabled = false;
            _progress.IsVisible = true;

            var tmp = Path.Combine(Path.GetTempPath(), "PriceCheckerUpdates");
            try
            {
                var progress = new Progress<double>(p =>
                {
                    Dispatcher.UIThread.Post(() => { _progress.Value = p; });
                });

                var path = await _checker.DownloadUpdateAsync(_info, tmp, progress, CancellationToken.None).ConfigureAwait(false);

                // Запустить инсталлятор или показать папку
                try
                {
                    var ext = Path.GetExtension(path)?.ToLowerInvariant() ?? string.Empty;
                    if (PriceCheckerAvalonia.Core.Services.PlatformHelper.IsWindows())
                    {
                        if (ext == ".exe" || ext == ".msi" || ext == ".bat")
                        {
                            var psi = new ProcessStartInfo(path) { UseShellExecute = true };
                            Process.Start(psi);
                            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life)
                                life.Shutdown();
                        }
                        else
                        {
                            // show in explorer
                            Process.Start(new ProcessStartInfo("explorer", $"/select,\"{path}\"") { UseShellExecute = true });
                        }
                    }
                    else if (PriceCheckerAvalonia.Core.Services.PlatformHelper.IsLinux())
                    {
                        if (ext == ".appimage")
                        {
                            // make executable then run
                            try
                            {
                                Process.Start(new ProcessStartInfo("chmod", $"+x \"{path}\"") { UseShellExecute = false });
                            }
                            catch { }
                            try
                            {
                                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                                if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life)
                                    life.Shutdown();
                            }
                            catch { }
                        }
                        else if (ext == ".deb" || ext == ".rpm")
                        {
                            // Show instruction dialog (installation requires privileges)
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                var dlg = new Window { Title = "Установка пакета", Width = 480, Height = 160, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                                var tb = new TextBlock { Text = $"Пакет скачан: {path}\nДля установки откройте терминал и выполните: sudo dpkg -i \"{path}\"", Margin = new Thickness(12) };
                                var ok = new Button { Content = "Открыть папку", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
                                ok.Click += (_, _) =>
                                {
                                    try { Process.Start(new ProcessStartInfo("xdg-open", $"\"{System.IO.Path.GetDirectoryName(path)}\"") { UseShellExecute = true }); } catch { }
                                    dlg.Close();
                                };
                                var sp = new StackPanel();
                                sp.Children.Add(tb);
                                sp.Children.Add(ok);
                                dlg.Content = sp;
                                dlg.ShowDialog(this);
                            });
                        }
                        else
                        {
                            // generic: open folder
                            try { Process.Start(new ProcessStartInfo("xdg-open", $"\"{System.IO.Path.GetDirectoryName(path)}\"") { UseShellExecute = true }); } catch { }
                        }
                    }
                    else if (PriceCheckerAvalonia.Core.Services.PlatformHelper.IsMac())
                    {
                        if (ext == ".dmg" || ext == ".pkg")
                        {
                            try { Process.Start(new ProcessStartInfo("open", $"\"{path}\"") { UseShellExecute = true }); } catch { }
                        }
                        else
                        {
                            try { Process.Start(new ProcessStartInfo("open", $"\"{System.IO.Path.GetDirectoryName(path)}\"") { UseShellExecute = true }); } catch { }
                        }
                    }
                    else
                    {
                        // Fallback: open folder
                        try { Process.Start(new ProcessStartInfo("xdg-open", $"\"{System.IO.Path.GetDirectoryName(path)}\"") { UseShellExecute = true }); } catch { }
                    }
                }
                catch
                {
                    // ignore launch errors
                }

                Close();
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var dlg = new Window { Title = "Ошибка", Width = 400, Height = 160, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                    var tb = new TextBlock { Text = "Ошибка загрузки обновления: " + ex.Message, Margin = new Thickness(12) };
                    var ok = new Button { Content = "Ок", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
                    ok.Click += (_, _) => dlg.Close();
                    var sp = new StackPanel();
                    sp.Children.Add(tb);
                    sp.Children.Add(ok);
                    dlg.Content = sp;
                    dlg.ShowDialog(this);
                });
            }
            finally
            {
                _downloadButton.IsEnabled = true;
                _laterButton.IsEnabled = true;
                _progress.IsVisible = false;
            }
        }
    }
}
