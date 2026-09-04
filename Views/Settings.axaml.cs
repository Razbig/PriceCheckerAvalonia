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
using System.Reflection;
using PriceCheckerAvalonia.Core.Model;

namespace PriceCheckerAvalonia.Views;

public partial class Settings : UserControl
{
    private readonly SettingsViewModel _vm;
    private PriceCheckerAvalonia.Core.Services.LocalDatabase? _coreDbInstance;

    public Settings()
    {
        InitializeComponent();

        // Resolve Core LocalDatabase from App if available.
        var app = Avalonia.Application.Current as App;
        var localDb = app?.LocalDb; // App.LocalDb may be PriceCheckerAvalonia.Core.Services.LocalDatabase

        PriceCheckerAvalonia.Core.Services.LocalDatabase? coreDb = null;
        if (localDb is PriceCheckerAvalonia.Core.Services.LocalDatabase cd)
        {
            coreDb = cd;
        }
        else if (localDb != null)
        {
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
                coreDb = null;
            }
        }
        else
        {
            // App.LocalDb is null — try to create core LocalDatabase from standard LocalApplicationData path
            try
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var appDir = System.IO.Path.Combine(localAppData, "PriceCheckerAvalonia");
                System.IO.Directory.CreateDirectory(appDir);
                var dbPath = System.IO.Path.Combine(appDir, "pricechecker.db");
                coreDb = new PriceCheckerAvalonia.Core.Services.LocalDatabase(dbPath);
            }
            catch (Exception ex)
            {
                // leave coreDb null but log for diagnostics
                Debug.WriteLine($"Failed to init core LocalDatabase in Settings: {ex.Message}");
            }
        }

        _coreDbInstance = coreDb;
        _vm = new SettingsViewModel(coreDb);
        DataContext = _vm;

        _ = _vm.LoadStoresFromRemoteAsync();
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

    private async void SaveSettings_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            string printType = "32";
            var r32 = this.FindControl<RadioButton>("PriceType32Radio");
            var r39 = this.FindControl<RadioButton>("PriceType39Radio");
            if (r39 != null && r39.IsChecked == true)
                printType = "39";
            else if (r32 != null && r32.IsChecked == true)
                printType = "32";

            if (_coreDbInstance != null)
            {
                try { _coreDbInstance.SetPrintTypeDefault(printType); } catch { }
            }

            var selected = _vm.SelectedStore;
            if (selected != null && _coreDbInstance != null)
            {
                try { _coreDbInstance.SetShop(selected.ShopId, selected.ShopName); } catch { }
            }

            try
            {
                if (selected != null)
                {
                    await FetchAndSaveMasterIpAsync(selected.ShopId);
                }
            }
            catch { }

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
                output.AppendLine($"Saved printType: {printType}");
                var sel = _vm.SelectedStore;
                if (sel != null)
                    output.AppendLine($"Selected store: {sel.ShopId} - {sel.ShopName}");
                else
                    output.AppendLine("Selected store: (none)");
                output.AppendLine("---");

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
            catch { }
        }
        catch { }

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
        catch { }
    }

    private async void CheckUpdates_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            var app = Avalonia.Application.Current as App;
            var checker = app?.UpdateChecker;
            if (checker == null)
            {
                await ShowSystemSettingsAsync("Update checker not initialized.");
                return;
            }

            const string serverUrl = "https://your-update-server.example.com"; // TODO: заменить на реальный URL
            var platform = PriceCheckerAvalonia.Core.Services.PlatformHelper.GetPlatformString();
            var info = await checker.CheckForUpdateAsync(serverUrl, channel: "stable", platform: platform);
            if (info == null)
            {
                await ShowSystemSettingsAsync("Оновлень не знайдено. Ваша версія актуальна.");
                return;
            }

            var current = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";
            if (!VersionHelper.IsNewerVersion(info.Version, current))
            {
                await ShowSystemSettingsAsync("Оновлень не знайдено. Ваша версія актуальна.");
                return;
            }

            var owner = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d
                ? d.MainWindow as Window
                : null;

            var wnd = new UpdateWindow(info, checker);
            await wnd.ShowDialog(owner!);
        }
        catch (Exception ex)
        {
            await ShowSystemSettingsAsync($"Помилка при перевірці оновлень: {ex.Message}");
        }
    }

    private async void ShowSystemSettings_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
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
        await dlg.ShowDialog(owner!);
    }

    private async void ShowBarSync_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (_coreDbInstance == null)
            {
                await ShowSystemSettingsAsync("Core local DB not available; cannot sync bars.");
                return;
            }

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

            progressDlg.Show(owner!);

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

    private async void ShowProductsImport_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (_coreDbInstance == null)
            {
                await ShowSystemSettingsAsync("Core local DB not available; cannot import products.");
                return;
            }

            var dlg = new Window
            {
                Title = "Import products",
                Width = 480,
                Height = 160,
                Content = new StackPanel
                {
                    Margin = new Thickness(10),
                    Children =
                    {
                        new TextBlock { Text = "Завантаження Products.json.gz..." },
                        new ProgressBar { IsIndeterminate = true, Height = 20, Margin = new Thickness(0,10,0,0) }
                    }
                }
            };

            var owner = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d
                ? d.MainWindow as Window
                : null;

            dlg.Show(owner!);

            try
            {
                var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
                var importer = new PriceCheckerAvalonia.Core.Services.ApiImporter(_coreDbInstance, logger);
                var url = "https://storge.almi.odesa.ua/pricechecker/menu/Products.json.gz";
                await importer.ImportFromGzipJsonUrlAsync(url, default);
            }
            catch (Exception ex)
            {
                dlg.Close();
                await ShowSystemSettingsAsync($"Error importing products: {ex.Message}");
                return;
            }

            dlg.Close();
            await ShowSystemSettingsAsync("Products import completed.");
        }
        catch { }
    }

    private async void ShowAdmin_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            if (_coreDbInstance == null)
            {
                await ShowSystemSettingsAsync("No local database instance available.");
                return;
            }

            var tables = new List<(string Name, long Count)>();
            using (var conn = _coreDbInstance.OpenConnection())
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    tables.Add((rdr.GetString(0), 0));
                }
            }

            // get counts
            for (int i = 0; i < tables.Count; i++)
            {
                var t = tables[i].Name;
                try
                {
                    using var c2 = _coreDbInstance.OpenConnection();
                    c2.Open();
                    using var cmd2 = c2.CreateCommand();
                    cmd2.CommandText = $"SELECT COUNT(1) FROM \"{t}\";";
                    var cntObj = cmd2.ExecuteScalar();
                    long cnt = 0;
                    if (cntObj is long l) cnt = l; else if (cntObj is int ii) cnt = ii; else if (cntObj != null && long.TryParse(cntObj.ToString(), out var p)) cnt = p;
                    tables[i] = (t, cnt);
                }
                catch { tables[i] = (t, -1); }
            }

            // build dialog with list of tables and buttons
            var listBox = new ListBox { Width = 560, Height = 300 };
            var listItems = new List<object>();
            foreach (var t in tables)
            {
                var txt = t.Count >= 0 ? $"{t.Name} ({t.Count})" : $"{t.Name} (count error)";
                var item = new ListBoxItem { Content = txt, Tag = t.Name };
                listItems.Add(item);
            }
            listBox.ItemsSource = listItems;

            var viewBtn = new Button { Content = "Просмотреть топ 1000", Width = 160, IsEnabled = false };
            var closeBtn = new Button { Content = "Закрыть", Width = 120 };

            listBox.SelectionChanged += (_, __) => { viewBtn.IsEnabled = listBox.SelectedItem != null; };
            viewBtn.Click += async (_, __) =>
            {
                if (listBox.SelectedItem is ListBoxItem lbi && lbi.Tag is string tbl)
                {
                    await ShowTableTopRowsAsync(tbl);
                }
            };

            var dialog = new Window
            {
                Title = "Database tables",
                Width = 600,
                Height = 420,
                Content = new StackPanel
                {
                    Margin = new Thickness(10),
                    Children =
                    {
                        new TextBlock { Text = "Выберите таблицу для просмотра (первые 1000 строк):", FontWeight = Avalonia.Media.FontWeight.Bold, Margin = new Thickness(0,0,0,8) },
                        listBox,
                        new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Thickness(0,8,0,0), Children = { viewBtn, closeBtn } }
                    }
                }
            };
            // attach close handler after dialog is created
            closeBtn.Click += (_, __) => dialog.Close();
            var owner = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d
                ? d.MainWindow as Window
                : null;

            await dialog.ShowDialog(owner!);
        }
        catch { }
    }

    private async Task ShowTableTopRowsAsync(string tableName)
    {
        if (_coreDbInstance == null) return;

        try
        {
            var sb = new StringBuilder();
            using var conn = _coreDbInstance.OpenConnection();
            conn.Open();
            using var cmd = conn.CreateCommand();
            // limit to 1000 rows
            cmd.CommandText = $"SELECT * FROM \"{tableName}\" LIMIT 1000;";
            using var rdr = cmd.ExecuteReader();
            var cols = new List<string>();
            for (int i = 0; i < rdr.FieldCount; i++) cols.Add(rdr.GetName(i));

            // header
            sb.AppendLine(string.Join(" | ", cols));

            int row = 0;
            while (rdr.Read())
            {
                var vals = new string[rdr.FieldCount];
                for (int i = 0; i < rdr.FieldCount; i++)
                {
                    try
                    {
                        if (rdr.IsDBNull(i)) vals[i] = "NULL";
                        else
                        {
                            var o = rdr.GetValue(i);
                            vals[i] = o?.ToString() ?? string.Empty;
                        }
                    }
                    catch { vals[i] = "(err)"; }
                }
                sb.AppendLine(string.Join(" | ", vals));
                row++;
            }

            if (row == 0) sb.AppendLine("(no rows)");

            await ShowSystemSettingsAsync($"Table: {tableName}\n---\n" + sb.ToString());
        }
        catch (Exception ex)
        {
            await ShowSystemSettingsAsync($"Error reading table {tableName}: {ex.Message}");
        }
    }
}
