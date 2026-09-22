using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using PriceCheckerAvalonia.Core.Model;
using PriceCheckerAvalonia.Helpers;

namespace PriceCheckerAvalonia.ViewModels
{
    /// <summary>
    /// Обгортка над Product для екрана друку цінників:
    /// додає кількість, команди +/−/видалити та готові тексти ціни/знижки.
    /// Сам Product (модель з БД) лишається "чистим" і без UI-логіки.
    /// </summary>
    public class ProductPrintItem : INotifyPropertyChanged
    {
        public Product Product { get; }

        public string Name => Product.Name;

        /// <summary>Показуємо артикул, а якщо його нема — штрихкод</summary>
        public string ArticleText => Product.Article?.ToString() ?? Product.Barcode;

        private Bitmap? _imageBitmap;
        public Bitmap? ImageBitmap
        {
            get
            {
                if (_imageBitmap is null && !string.IsNullOrWhiteSpace(Product.ImagePath) && File.Exists(Product.ImagePath))
                {
                    try
                    {
                        _imageBitmap = new Bitmap(Product.ImagePath);
                    }
                    catch
                    {
                        // якщо файл пошкоджений/не картинка — просто без фото
                        _imageBitmap = null;
                    }
                }
                return _imageBitmap;
            }
        }

        private int _quantity = 1;
        public int Quantity
        {
            get => _quantity;
            set
            {
                if (value < 1) value = 1;
                if (_quantity == value) return;
                _quantity = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DiscountText));
                ((RelayCommand)DecreaseCommand).RaiseCanExecuteChanged();
            }
        }

        /// <summary>Ціна за одну одиницю товару</summary>
        public string PriceText => Product.Price.ToString("0.00").Replace('.', ',');

        /// <summary>Розмір знижки за одну одиницю товару, якщо є стара ціна</summary>
        public string DiscountText => Product.PriceOld > Product.Price
            ? $"–{(Product.PriceOld - Product.Price):0.00}".Replace('.', ',')
            : string.Empty;

        public event Action<ProductPrintItem>? RemoveRequested;

        public ICommand IncreaseCommand { get; }
        public ICommand DecreaseCommand { get; }
        public ICommand RemoveCommand { get; }

        public ProductPrintItem(Product product, int quantity = 1)
        {
            Product = product ?? throw new ArgumentNullException(nameof(product));
            _quantity = quantity < 1 ? 1 : quantity;

            IncreaseCommand = new RelayCommand(() => Quantity++);
            DecreaseCommand = new RelayCommand(() => Quantity--, () => Quantity > 1);
            RemoveCommand = new RelayCommand(() => RemoveRequested?.Invoke(this));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}