using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradingApp.Constants
{
    public static class ApiEndpoints
    {
        public const string PlaceStopOrder = "https://trade-api.finam.ru/api/v1/stops";

        public const string GetOrderBook = "https://invest-public-api.tinkoff.ru";

        public const string Candles = "https://trade-api.finam.ru/api/v1/intraday-candles";
    }
}
