using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using System;
using PriceCheckerAvalonia.Core.Model;

namespace PriceCheckerAvalonia.Views;

public partial class ProductInfo : UserControl
{
    public ProductInfo()
    {
        InitializeComponent();
    }

    public void SetProduct(Product product)
    {
        if (product is null) return;

        ProductName.Text = product.Name ?? string.Empty;
        ProductArticle.Text = $"артикул: {product.Article} / в наличии {product.StockQty} шт.";
        ProductPrice.Text = string.Format("{0:0.00} ₴", product.Price);
        ProductType.Text = string.IsNullOrEmpty(product.ProductType) ? "Вид продукції: -" : $"Вид продукції: {product.ProductType}";
        Brand.Text = string.IsNullOrEmpty(product.Brand) ? "" : $"Торгова марка: {product.Brand}";

        if (!string.IsNullOrEmpty(product.ImagePath))
        {
            try
            {
                ProductImage.Source = new Bitmap(product.ImagePath);
            }
            catch
            {
                // ignore image loading errors
            }
        }
    }
}