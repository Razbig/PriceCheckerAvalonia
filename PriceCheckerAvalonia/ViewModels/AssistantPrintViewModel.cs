using System.Collections.ObjectModel;
using System.ComponentModel;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using PriceCheckerAvalonia.Core.Services;
using PriceCheckerAvalonia.Core.Model;
using PriceCheckerAvalonia.Helpers;

namespace PriceCheckerAvalonia.ViewModels
{
    public class AssistantPrintViewModel : INotifyPropertyChanged
    {
        private static readonly HttpClient Http = new();
        private const string PrintEndpoint = "https://pim.almi.odesa.ua/RetailHelper/hs/mobile/print";
        private const string BasicCredentials = "UHJpY2VDaGVja2VyOlBhc3NQcmljZUNoZWNrZXI=";

        public ObservableCollection<ProductPrintItem> Items { get; } = new();

        public ICommand PrintCommand { get; }
        public ICommand ClearAllCommand { get; }
        public string PrintStatus { get; private set; } = string.Empty;
        public bool IsPrinting { get; private set; }

        private readonly RelayCommand _printCommand;

        public AssistantPrintViewModel()
        {
            _printCommand = new RelayCommand(OnPrint, () => !IsPrinting);
            PrintCommand = _printCommand;
            ClearAllCommand = new RelayCommand(() => Items.Clear());
        }

        /// <summary>
        /// Викликайте це зі сканера штрихкодів / логіки пошуку товару.
        /// Якщо товар з таким Id вже в списку — просто збільшує кількість.
        /// </summary>
        public void AddProduct(Product product, int quantity = 1)
        {
            var existing = FindByBarcode(product.Barcode);
            if (existing != null)
            {
                existing.Quantity += quantity;
                return;
            }

            var item = new ProductPrintItem(product, quantity);
            item.RemoveRequested += Item_RemoveRequested;
            Items.Add(item);
        }

        public void AddTestProducts()
        {
            Items.Clear();
            AddProduct(new Product
            {
                Id = 1,
                Article = 12345,
                Barcode = "12345",
                Name = "Тестовий товар",
                Price = 99.99,
                UpdatedAt = DateTime.UtcNow
            });
            AddProduct(new Product
            {
                Id = 2,
                Article = 123562,
                Barcode = "123562",
                Name = "Тестовий товар 2",
                Price = 89.99,
                PriceOld = 199.99,
                UpdatedAt = DateTime.UtcNow
            });
        }

        private ProductPrintItem? FindByBarcode(string barcode)
        {
            if (string.IsNullOrWhiteSpace(barcode))
                return null;

            foreach (var item in Items)
            {
                if (string.Equals(item.Product.Barcode.Trim(), barcode.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                    return item;
            }
            return null;
        }

        private void Item_RemoveRequested(ProductPrintItem item)
        {
            item.RemoveRequested -= Item_RemoveRequested;
            Items.Remove(item);
        }

        private async void OnPrint()
        {
            if (Items.Count == 0)
            {
                SetPrintStatus("Додайте товари перед відправленням на друк.");
                return;
            }

            var localDb = (Application.Current as App)?.LocalDb;
            if (localDb == null)
            {
                SetPrintStatus("Локальна база даних недоступна.");
                return;
            }

            var shopId = localDb.GetShopId();
            if (shopId == 0)
            {
                SetPrintStatus("Спочатку виберіть магазин у налаштуваннях.");
                return;
            }

            var barcodes = Items
                .Where(item => !string.IsNullOrWhiteSpace(item.Product.Barcode))
                .SelectMany(item => Enumerable.Repeat(item.Product.Barcode.Trim(), item.Quantity))
                .ToArray();

            if (barcodes.Length == 0)
            {
                SetPrintStatus("У списку немає штрихкодів для друку.");
                return;
            }

            IsPrinting = true;
            OnPropertyChanged(nameof(IsPrinting));
            _printCommand.RaiseCanExecuteChanged();
            SetPrintStatus(string.Empty);

            try
            {
                var printType = localDb.GetPrintTypeDefault() == "39" ? "39mm" : "32mm";
                var payload = new
                {
                    stock = shopId.ToString(),
                    deviceID = Environment.MachineName,
                    user = "Price checker",
                    article = string.Empty,
                    barcode = barcodes,
                    size = printType
                };

                using var request = new HttpRequestMessage(HttpMethod.Post, PrintEndpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", BasicCredentials);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

                using var response = await Http.SendAsync(request);
                response.EnsureSuccessStatusCode();

                Items.Clear();
                SetPrintStatus("Цінники успішно відправлено на друк.");
            }
            catch (HttpRequestException ex)
            {
                SetPrintStatus($"Не вдалося відправити цінники на друк: {ex.Message}");
            }
            catch (Exception ex)
            {
                SetPrintStatus($"Помилка під час відправлення на друк: {ex.Message}");
            }
            finally
            {
                IsPrinting = false;
                OnPropertyChanged(nameof(IsPrinting));
                _printCommand.RaiseCanExecuteChanged();
            }
        }

        private void SetPrintStatus(string status)
        {
            PrintStatus = status;
            OnPropertyChanged(nameof(PrintStatus));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}