namespace PriceCheckerAvalonia.Model
{
    public class Store
    {
        public int ShopId { get; set; }
        public string ShopName { get; set; } = string.Empty;
        public override string ToString() => ShopName;
    }
}
