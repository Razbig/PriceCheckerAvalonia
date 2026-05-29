
using System;

public class Product
{
    public long Id { get; set; }
    public int Article { get; set; }
    public string Barcode { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Category { get; set; } = "";
    public string Country { get; set; } = "";
    public string Brand { get; set; } = "";
    public string ProductType { get; set; } = "";
    public int StockQty { get; set; }
    public string? ImagePath { get; set; }
    public DateTime UpdatedAt { get; set; }
}