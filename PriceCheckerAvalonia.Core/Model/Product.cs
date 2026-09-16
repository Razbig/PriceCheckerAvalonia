using System;

namespace PriceCheckerAvalonia.Core.Model
{
    public class Product
    {
        public int Id { get; set; }
        // Артикул товара (соответствует id_article в t_bar)
        public int? Article { get; set; }
        public string Barcode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public double Price { get; set; }
        public string? Category { get; set; }
        public string? Country { get; set; }
        public string? Brand { get; set; }
        public string? ProductType { get; set; }
        public int StockQty { get; set; }
        public string? ImagePath { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}