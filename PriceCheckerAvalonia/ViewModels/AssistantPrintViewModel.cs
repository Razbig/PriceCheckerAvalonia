using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System;
using System.Windows.Input;
using PriceCheckerAvalonia.Core.Model;
using PriceCheckerAvalonia.Helpers;

namespace PriceCheckerAvalonia.ViewModels
{
    public class AssistantPrintViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<ProductPrintItem> Items { get; } = new();

        public ICommand PrintCommand { get; }
        public ICommand ClearAllCommand { get; }

        public AssistantPrintViewModel()
        {
            PrintCommand = new RelayCommand(OnPrint);
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

        private void OnPrint()
        {
            // TODO: логіка відправки цінників на друк
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}