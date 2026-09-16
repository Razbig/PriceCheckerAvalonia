using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Velopack;
using PriceCheckerAvalonia.Services;
using static System.Net.Mime.MediaTypeNames;

namespace PriceCheckerAvalonia
{
    public class UpdateWindow : Window
    {
        private readonly TextBlock _header;
        private readonly TextBlock _notes;
        private readonly ProgressBar _progress;
        private readonly Button _downloadButton;
        private readonly Button _laterButton;
        private readonly Velopack.UpdateInfo _info;
        private readonly UpdateService _updateService;

        public UpdateWindow(
            Velopack.UpdateInfo info,
            UpdateService updateService)
        {
            _info = info ?? throw new ArgumentNullException(nameof(info));
            _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));

            Title = "Доступне оновлення";
            Width = 700;
            Height = 500;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var panel = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12
            };

            _header = new TextBlock
            {
                FontSize = 18,
                FontWeight = Avalonia.Media.FontWeight.Bold,
                Text = $"Нова версія: {_info.TargetFullRelease.Version}"
            };

            panel.Children.Add(_header);

            _notes = new TextBlock
            {
                Text = string.Empty
            };

            var scrollViewer = new ScrollViewer
            {
                Content = _notes,
                Height = 250
            };

            panel.Children.Add(scrollViewer);

            _progress = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Height = 18,
                IsVisible = false
            };

            panel.Children.Add(_progress);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8
            };

            _laterButton = new Button
            {
                Content = "Пізніше"
            };

            _laterButton.Click += LaterButton_Click;
            buttons.Children.Add(_laterButton);

            _downloadButton = new Button
            {
                Content = "Завантажити та встановити"
            };

            _downloadButton.Click += DownloadButton_Click;
            buttons.Children.Add(_downloadButton);

            panel.Children.Add(buttons);

            Content = panel;
        }

        private void LaterButton_Click(
            object? sender,
            RoutedEventArgs e)
        {
            Close();
        }

        private async void DownloadButton_Click(
            object? sender,
            RoutedEventArgs e)
        {
            _downloadButton.IsEnabled = false;
            _laterButton.IsEnabled = false;
            _progress.IsVisible = true;

            try
            {
                var progress = new Action<int>(percent =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        _progress.Value = percent;
                    });
                });

                var downloaded = await _updateService
                    .DownloadUpdateAsync(
                        _info,
                        progress,
                        CancellationToken.None);

                if (!downloaded)
                {
                    await ShowErrorAsync(
                        "Не вдалося завантажити оновлення.");

                    return;
                }

                _updateService.ApplyUpdateAndRestart(_info);
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(
                    $"Помилка оновлення: {ex.Message}");
            }
            finally
            {
                _downloadButton.IsEnabled = true;
                _laterButton.IsEnabled = true;
                _progress.IsVisible = false;
            }
        }

        private async Task ShowErrorAsync(string message)
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dlg = new Window
                {
                    Title = "Помилка",
                    Width = 450,
                    Height = 180,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var okButton = new Button
                {
                    Content = "OK",
                    Width = 100,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var panel = new StackPanel
                {
                    Margin = new Thickness(12),
                    Spacing = 12
                };

                panel.Children.Add(new TextBlock
                {
                    Text = message,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                });

                panel.Children.Add(okButton);

                dlg.Content = panel;

                okButton.Click += (_, _) => dlg.Close();

                await dlg.ShowDialog(this!);
            });
        }
    }
}