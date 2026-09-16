using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using PriceCheckerAvalonia.Core.Model;
using PriceCheckerAvalonia.Core.Services;

namespace PriceCheckerAvalonia.ViewModels
{
    public class SettingsViewModel : INotifyPropertyChanged
    {
        private readonly LocalDatabase? _localDb;
        public ObservableCollection<Store> Stores { get; } = new();

        private bool _isPrint32 = true;
        public bool IsPrint32
        {
            get => _isPrint32;
            set
            {
                if (_isPrint32 == value) return;
                _isPrint32 = value;
                // persist
                if (_localDb != null)
                    _localDb.SetPrintTypeDefault(value ? "32" : "39");
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPrint39));
            }
        }

        public bool IsPrint39
        {
            get => !_isPrint32;
            set
            {
                IsPrint32 = !value;
            }
        }

        private Store? _selectedStore;
        public Store? SelectedStore
        {
            get => _selectedStore;
            set
            {
                if (_selectedStore == value) return;
                _selectedStore = value;
                if (value != null && _localDb != null)
                {
                    _localDb.SetShop(value.ShopId, value.ShopName);
                }
                OnPropertyChanged();
            }
        }

        public SettingsViewModel(LocalDatabase? localDb)
        {
            _localDb = localDb;
            LoadLocalStores();
            // initialize print type from settings
            try
            {
                var pt = _localDb?.GetPrintTypeDefault() ?? "32";
                _isPrint32 = pt == "32";
            }
            catch
            {
                _isPrint32 = true;
            }
            // notify bindings about initial values
            OnPropertyChanged(nameof(IsPrint32));
            OnPropertyChanged(nameof(IsPrint39));
        }

        private void LoadLocalStores()
        {
            if (_localDb == null) return;
            var list = _localDb.GetStores();
            Stores.Clear();
            foreach (var s in list)
                Stores.Add(s);

            var id = _localDb.GetShopId();
            if (id != 0)
            {
                var found = Stores.FirstOrDefault(x => x.ShopId == id);
                if (found != null)
                {
                    SelectedStore = found;
                }
                else
                {
                    // If store id saved but not present in stores table, create a temporary entry
                    var name = _localDb.GetShopName();
                    var temp = new Store { ShopId = id, ShopName = name };
                    Stores.Add(temp);
                    SelectedStore = temp;
                }
            }
        }

        public async Task LoadStoresFromRemoteAsync()
        {
            try
            {
                using var http = new HttpClient();
                var resp = await http.GetAsync("https://pim.almi.odesa.ua/PIMOpen/hs/mobile/stores");
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync();

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                List<Store> stores = new();
                try
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        stores = JsonSerializer.Deserialize<List<Store>>(root.GetRawText(), options) ?? new List<Store>();
                    }
                    else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("shops", out var shopsProp) && shopsProp.ValueKind == JsonValueKind.Array)
                    {
                        stores = JsonSerializer.Deserialize<List<Store>>(shopsProp.GetRawText(), options) ?? new List<Store>();
                    }
                    else
                    {
                        // Fallback: try to deserialize root as list
                        stores = JsonSerializer.Deserialize<List<Store>>(json, options) ?? new List<Store>();
                    }
                }
                catch
                {
                    stores = new List<Store>();
                }

                if (_localDb != null)
                {
                    _localDb.UpsertStores(stores);
                    Stores.Clear();
                    foreach (var s in _localDb.GetStores())
                        Stores.Add(s);
                    // restore selected store from saved id if present
                    var id = _localDb.GetShopId();
                    if (id != 0)
                        SelectedStore = Stores.FirstOrDefault(x => x.ShopId == id);
                }
            }
            catch
            {
                // ignore network errors for now
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
