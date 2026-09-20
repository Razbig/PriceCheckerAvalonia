using System;

namespace PriceCheckerAvalonia.Core.Model
{
    public class t_bar
    {
        public long id_bar { get; set; }
        public int id_article { get; set; }
        public int id_measure { get; set; }
        public string bar { get; set; } = "";
        public int dtype { get; set; }
        public string memo { get; set; } = "";
        public decimal price { get; set; }

    }

}
