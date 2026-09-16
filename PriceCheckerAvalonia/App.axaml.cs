using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System;
using System.IO;
using PriceCheckerAvalonia.Core.Services;

namespace PriceCheckerAvalonia
{
    public partial class App : Application
    {
        public LocalDatabase? LocalDb { get; private set; }
        public PriceCheckerAvalonia.Services.UpdateService? UpdateService { get; private set; }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // Initialize local database in user local app data
            try
            {
                var localAppData = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);

                var appDir = Path.Combine(
                    localAppData,
                    "PriceCheckerAvalonia");

                Directory.CreateDirectory(appDir);

                var dbPath = Path.Combine(
                    appDir,
                    "pricechecker.db");

                LocalDb = new LocalDatabase(dbPath);
            }
            catch
            {
                // If DB init fails, continue without it.
                LocalDb = null;
            }

            // Initialize Velopack update service
            try
            {
                UpdateService = new PriceCheckerAvalonia.Services.UpdateService();
            }
            catch
            {
                UpdateService = null;
            }

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }

            // Check for updates in background without blocking UI
            try
            {
                _ = CheckForUpdatesAtStartupAsync();
            }
            catch
            {
                // Ignore update check errors during startup
            }

            base.OnFrameworkInitializationCompleted();
        }

        private async System.Threading.Tasks.Task CheckForUpdatesAtStartupAsync()
        {
            try
            {
                var updateService = UpdateService;
                if (updateService == null)
                    return;

                var info = await updateService
                    .CheckForUpdatesAsync()
                    .ConfigureAwait(false);

                if (info == null)
                    return;

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        var wnd = new UpdateWindow(
                            info,
                            updateService);

                        _ = wnd.ShowDialog(desktop.MainWindow);
                    }
                });
            }
            catch
            {
                // Ignore update check errors during startup
            }
        }
    }
}