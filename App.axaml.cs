using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using System.IO;
using PriceCheckerAvalonia.Core.Services;

namespace PriceCheckerAvalonia
{
    public partial class App : Application
    {
        public LocalDatabase? LocalDb { get; private set; }

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

            base.OnFrameworkInitializationCompleted();
        }
    }
}