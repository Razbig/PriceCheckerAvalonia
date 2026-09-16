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

        // Reset image and try to load from local file if available.
        ProductImage.Source = null;
        if (!string.IsNullOrEmpty(product.ImagePath))
        {
            try
            {
                var path = product.ImagePath;
                if (System.IO.File.Exists(path))
                {
                    // Read bytes and create MemoryStream without disposing it so Bitmap can access data
                    var bytes = System.IO.File.ReadAllBytes(path);
                    var ms2 = new System.IO.MemoryStream(bytes);
                    ProductImage.Source = new Bitmap(ms2);
                }
                else
                {
                    // For remote URLs or missing files we don't attempt to load here.
                }
            }
            catch
            {
                // ignore image loading errors
            }
        }
    }

    public void SetImageFromFile(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        ProductImage.Source = null;
        try
        {
            if (System.IO.File.Exists(path))
            {
                var bytes = System.IO.File.ReadAllBytes(path);
                var ms2 = new System.IO.MemoryStream(bytes);
                ProductImage.Source = new Bitmap(ms2);
            }
        }
        catch
        {
            // ignore
        }
    }
}