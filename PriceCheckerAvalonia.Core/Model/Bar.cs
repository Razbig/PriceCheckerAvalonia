using System;

namespace PriceCheckerAvalonia.Core.Model
{
    public class Bar
    {
        public long IdBar { get; set; }
        public int IdArticle { get; set; }
        public int IdMeasure { get; set; }
        public string BarValue { get; set; } = ""; // maps from column 'bar'
        public int Dtype { get; set; }
        public string Memo { get; set; } = "";
    }
}
