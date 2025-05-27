using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradingApp.Models
{
    public class Order
    {
        public long OrderNo { get; set; }    // было string OrderId
        public long TransactionId { get; set; }
        public string SecurityCode { get; set; }
        public string SecurityBoard { get; set; }
        public string BuySell { get; set; }
        public double Price { get; set; }
        public int Quantity { get; set; }
        public string Status { get; set; }
    }
}
