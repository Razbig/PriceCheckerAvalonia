using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System;
using System.Linq;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Avalonia.Styling;
using PriceCheckerAvalonia.ViewModels;
using PriceCheckerAvalonia.Core.Services;

namespace PriceCheckerAvalonia.Views;

public partial class Settings : UserControl
{
    private readonly SettingsViewModel _vm;
    private PriceCheckerAvalonia.Core.Services.LocalDatabase? _coreDbInstance;

    public Settings()
    {
        InitializeComponent();
        // Resolve Core LocalDatabase from App if available
        var app = Avalonia.Application.Current as App;
        var localDb = app?.LocalDb; // App.LocalDb is PriceCheckerAvalonia.Core.Services.LocalDatabase
        // Our ViewModel expects PriceCheckerAvalonia.Core.Services.LocalDatabase from the core project.
        // If App.LocalDb is of the core type (when using core project), pass it; otherwise null.
        PriceCheckerAvalonia.Core.Services.LocalDatabase? coreDb = null;
        if (localDb is PriceCheckerAvalonia.Core.Services.LocalDatabase cd)
            coreDb = cd;
        else if (localDb != null)
        {
            // App.LocalDb is the project implementation; try to reuse the same DB file by
            // reading its connection string and creating a core LocalDatabase instance.
            try
            {
                var conn = localDb.OpenConnection();
                var csb = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(conn.ConnectionString);
                var dbPath = csb.DataSource;
                if (!string.IsNullOrEmpty(dbPath))
                {
                    coreDb = new PriceCheckerAvalonia.Core.Services.LocalDatabase(dbPath);
                }
            }
            catch
            {
                // fallback to null if anything fails
                coreDb = null;
            }
        }
        _coreDbInstance = coreDb;
        _vm = new SettingsViewModel(coreDb);
        DataContext = _vm;

        // Fire-and-forget remote load (ignore errors)
        _ = _vm.LoadStoresFromRemoteAsync();

        // Print type is bound to SettingsViewModel (IsPrint32 / IsPrint39) and persisted there
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async Task FetchAndSaveMasterIpAsync(int shopId)
    {
        if (_coreDbInstance == null) return;

        try
        {
            using var http = new HttpClient();
            // Basic auth header (Base64 string from provided example)
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", "UHJpY2VDaGVja2VyOlBhc3NQcmljZUNoZWNrZXI=");
            http.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var payload = new { stock = shopId.ToString() };
            var json = JsonSerializer.Serialize(payload);

            var req = new HttpRequestMessage(HttpMethod.Get, "https://pim.almi.odesa.ua/RetailHelper/hs/api/shops/getMaster")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            var resp = await http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync();

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("master_ip", out var mip))
                {
                    var masterIp = mip.GetString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(masterIp))
                    {
                        _coreDbInstance.SetSetting("master_ip", masterIp);
                    }
                }
            }
            catch
            {
                // ignore parsing
            }
        }
        catch
        {
            // ignore network errors
        }
    }

    private async void SaveSettings_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Save selected store via ViewModel (it already writes shop id/name on set)
        // Save selected print type to local DB if available
        try
        {
            // Determine selected print type from radio buttons directly to avoid binding timing issues
            string printType = "32";
            var r32 = this.FindControl<RadioButton>("PriceType32Radio");
            var r39 = this.FindControl<RadioButton>("PriceType39Radio");
            if (r39 != null && r39.IsChecked == true)
                printType = "39";
            else if (r32 != null && r32.IsChecked == true)
                printType = "32";

            // Persist print type to core DB instance if available
            if (_coreDbInstance != null)
            {
                try { _coreDbInstance.SetPrintTypeDefault(printType); } catch { }
            }

            // Selected store may not have been persisted by ViewModel if it received null DB — persist here
            var selected = _vm.SelectedStore;
            if (selected != null && _coreDbInstance != null)
            {
                try { _coreDbInstance.SetShop(selected.ShopId, selected.ShopName); } catch { }
            }

            // After persisting shop, try to fetch master_ip and save it
            try
            {
                if (selected != null)
                {
                    await FetchAndSaveMasterIpAsync(selected.ShopId);
                }
            }
            catch
            {
                // ignore
            }

            // After saving, read contents of sync_meta and show in a modal popup
            try
            {
                var sb = new StringBuilder();
                if (_coreDbInstance != null)
                {
                    using var conn = _coreDbInstance.OpenConnection();
                    conn.Open();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT key, value FROM sync_meta";
                    using var rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        var k = rdr.IsDBNull(0) ? string.Empty : rdr.GetString(0);
                        var v = rdr.IsDBNull(1) ? string.Empty : rdr.GetString(1);
                        sb.AppendLine($"{k} = {v}");
                        Debug.WriteLine($"sync_meta: {k} = {v}");
                    }
                }

                var output = new StringBuilder();

                // Prepend what we just saved
                output.AppendLine($"Saved printType: {printType}");
                var sel = _vm.SelectedStore;
                if (sel != null)
                    output.AppendLine($"Selected store: {sel.ShopId} - {sel.ShopName}");
                else
                    output.AppendLine("Selected store: (none)");
                output.AppendLine("---");

                // Convenience getters from DB (if available)
                try
                {
                    if (_coreDbInstance != null)
                    {
                        output.AppendLine($"GetShopId(): {_coreDbInstance.GetShopId()}");
                        output.AppendLine($"GetShopName(): {_coreDbInstance.GetShopName()}");
                        output.AppendLine($"GetPrintTypeDefault(): {_coreDbInstance.GetPrintTypeDefault()}");
                        output.AppendLine($"master_ip (GetSetting): {_coreDbInstance.GetSetting("master_ip")}");
                    }
                    else
                    {
                        var app = Avalonia.Application.Current as App;
                        var ad = app?.LocalDb;
                        if (ad != null)
                        {
                            // ad is likely core LocalDatabase
                            output.AppendLine($"GetShopId(): {ad.GetShopId()}");
                            output.AppendLine($"GetShopName(): {ad.GetShopName()}");
                            output.AppendLine($"GetPrintTypeDefault(): {ad.GetPrintTypeDefault()}");
                            output.AppendLine($"master_ip (GetSetting): {ad.GetSetting("master_ip")}");
                        }
                    }
                }
                catch { }

                output.AppendLine("--- sync_meta rows ---");
                if (sb.Length > 0)
                    output.Append(sb.ToString());
                else
                    output.AppendLine("(sync_meta is empty)");

                await ShowSystemSettingsAsync(output.ToString());
            }
            catch
            {
                // ignore read/display errors
            }
        }
        catch
        {
            // ignore any errors saving settings
        }

            // Close settings frame by finding MainWindow and hiding SettingsFrame
        try
        {
            var mw = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d
                ? d.MainWindow as MainWindow
                : null;
            if (mw != null)
            {
                mw.HideBlurDialog();
                mw.SettingsFrame.IsVisible = false;
                mw.SettingsFrame.Content = null;
                mw.SetMainFrameVisible(true);
            }
        }
        catch
        {
            // ignore
        }
    }

    private async void ShowSystemSettings_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // read current settings and show popup
        try
        {
            var sb = new StringBuilder();
            if (_coreDbInstance != null)
            {
                sb.AppendLine($"GetShopId(): {_coreDbInstance.GetShopId()}");
                sb.AppendLine($"GetShopName(): {_coreDbInstance.GetShopName()}");
                sb.AppendLine($"GetPrintTypeDefault(): {_coreDbInstance.GetPrintTypeDefault()}");
                sb.AppendLine($"master_ip: {_coreDbInstance.GetSetting("master_ip")} ");
            }
            else
            {
                var app = Avalonia.Application.Current as App;
                var ad = app?.LocalDb;
                if (ad != null)
                {
                    sb.AppendLine($"GetShopId(): {ad.GetShopId()}");
                    sb.AppendLine($"GetShopName(): {ad.GetShopName()}");
                    sb.AppendLine($"GetPrintTypeDefault(): {ad.GetPrintTypeDefault()}");
                    sb.AppendLine($"master_ip: {ad.GetSetting("master_ip")} ");
                }
            }

            sb.AppendLine("--- sync_meta rows ---");
            if (_coreDbInstance != null)
            {
                using var conn = _coreDbInstance.OpenConnection();
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT key, value FROM sync_meta";
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    var k = rdr.IsDBNull(0) ? string.Empty : rdr.GetString(0);
                    var v = rdr.IsDBNull(1) ? string.Empty : rdr.GetString(1);
                    sb.AppendLine($"{k} = {v}");
                }
            }

            await ShowSystemSettingsAsync(sb.ToString());
        }
        catch { }
    }

    private async Task ShowSystemSettingsAsync(string output)
    {
        var dlg = new Window
        {
            Title = "System settings",
            Width = 700,
            Height = 400,
            Content = new StackPanel
            {
                Margin = new Thickness(10),
                Children =
                {
                    new ScrollViewer
                    {
                        Content = new TextBlock { Text = output },
                        Height = 320
                    },
                    new Button
                    {
                        Content = "Close",
                        Width = 120,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        Margin = new Thickness(0,8,0,0)
                    }
                }
            }
        };

        if (dlg.Content is StackPanel sp && sp.Children.Count >= 2 && sp.Children[1] is Button closeBtn)
            closeBtn.Click += (_, _) => dlg.Close();

        var owner = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d
            ? d.MainWindow as Window
            : null;
        await dlg.ShowDialog(owner);
    }

    private async void ShowBarSync_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (_coreDbInstance == null)
            {
                await ShowSystemSettingsAsync("Core local DB not available; cannot sync bars.");
                return;
            }

            // show simple progress dialog
            var progressDlg = new Window
            {
                Title = "Sync t_bar",
                Width = 400,
                Height = 160,
                Content = new StackPanel
                {
                    Margin = new Thickness(10),
                    Children =
                    {
                        new TextBlock { Text = "Виконується завантаження t_bar..." },
                        new ProgressBar { IsIndeterminate = true, Height = 20, Margin = new Thickness(0,10,0,0) }
                    }
                }
            };

            var owner = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d
                ? d.MainWindow as Window
                : null;

            // show progress non-modally
            progressDlg.Show(owner);

            int count = 0;
            try
            {
                count = await BarSyncHelper.FetchAndUpsertBarsAsync(_coreDbInstance);
            }
            catch (Exception ex)
            {
                progressDlg.Close();
                await ShowSystemSettingsAsync($"Error during t_bar sync: {ex.Message}");
                return;
            }

            progressDlg.Close();
            await ShowSystemSettingsAsync($"t_bar sync completed. Rows fetched: {count}");
        }
        catch { }
    }

    private async void ShowAdmin_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Database tables and row counts:");
            if (_coreDbInstance != null)
            {
                using var conn = _coreDbInstance.OpenConnection();
                conn.Open();
                using var cmd = conn.CreateCommand();
                // enumerate tables from sqlite_master
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
                using var rdr = cmd.ExecuteReader();
                var tables = new List<string>();
                while (rdr.Read())
                {
                    tables.Add(rdr.GetString(0));
                }

                foreach (var t in tables)
                {
                    using var c2 = _coreDbInstance.OpenConnection();
                    c2.Open();
                    using var cmd2 = c2.CreateCommand();
                    cmd2.CommandText = $"SELECT COUNT(1) FROM \"{t}\";";
                    var cnt = cmd2.ExecuteScalar();
                    sb.AppendLine($"{t}: {cnt}");
                }
            }
            else
            {
                sb.AppendLine("No local database instance available.");
            }

            await ShowSystemSettingsAsync(sb.ToString());
        }
        catch { }
    }
}
