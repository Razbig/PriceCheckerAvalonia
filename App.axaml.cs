using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using System.IO;
using PriceCheckerAvalonia.Core.Services;
using Avalonia.Threading;
using System.Reflection;
using Avalonia.Controls.ApplicationLifetimes;

namespace PriceCheckerAvalonia
{
    public partial class App : Application
    {
        public LocalDatabase? LocalDb { get; private set; }
        public PriceCheckerAvalonia.Core.Services.UpdateChecker? UpdateChecker { get; private set; }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // Initialize local database in user local app data
            try
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var appDir = Path.Combine(localAppData, "PriceCheckerAvalonia");
                Directory.CreateDirectory(appDir);
                var dbPath = Path.Combine(appDir, "pricechecker.db");
                LocalDb = new LocalDatabase(dbPath);
                // Initialize update checker with a shared HttpClient
                try
                {
                    UpdateChecker = new PriceCheckerAvalonia.Core.Services.UpdateChecker(new System.Net.Http.HttpClient());
                }
                catch
                {
                    UpdateChecker = null;
                }
            }
            catch
            {
                // If DB init fails, continue without it.
                LocalDb = null;
            }

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }

            // Запустить проверку обновлений в фоне (не блокируя UI)
            try
            {
                // Не await — пусть выполняется в фоне
                _ = CheckForUpdatesAtStartupAsync();
            }
            catch
            {
                // Игнорируем любые ошибки проверки обновлений при старте
            }

            base.OnFrameworkInitializationCompleted();
        }

        private async System.Threading.Tasks.Task CheckForUpdatesAtStartupAsync()
        {
            try
            {
                // Поменяйте на ваш URL обновлений
                const string serverUrl = "https://your-update-server.example.com";
                var checker = UpdateChecker;
                if (checker == null) return;

                var platform = PriceCheckerAvalonia.Core.Services.PlatformHelper.GetPlatformString();
                var info = await checker.CheckForUpdateAsync(serverUrl, channel: "stable", platform: platform).ConfigureAwait(false);
                if (info == null) return;

                // Сравнить версии (простая семантическая проверка)
                var current = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";
                if (PriceCheckerAvalonia.Core.Services.VersionHelper.IsNewerVersion(info.Version, current))
                {
                    // Показать окно обновления на UI-потоке
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                        {
                            var wnd = new UpdateWindow(info, checker);
                            wnd.ShowDialog(desktop.MainWindow);
                        }
                    });
                }
            }
            catch
            {
                // Игнорируем ошибки проверки
            }
        }
    }
}