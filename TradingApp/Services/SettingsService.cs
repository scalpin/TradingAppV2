using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradingApp.Services
{
    public class SettingsService
    {
        public string fToken { get; set; } = "CAEQ36PDCBoYphIlMaaXcYzx2Wpl2nvUx+/juue4k0AR";
        public string tToken { get; set; } = "t.fvdclBoZKw_MlDraPaWfM7gVlzhybv4-_hSItZLyQEIDuXW7r8jNBlWBbAi4kwDQpLlyl6PX3EMhZ5edlpKx5A";
        public string ClientId { get; set; } = "707190RBMU2";

        // Словарь «тикер → FIGI»
        private readonly Dictionary<string, string> _tickerToFigi = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "SBER", "BBG004730N88" },
            { "GAZP", "BBG000C6K6G9" },
            { "LKOH", "BBG004770R77" },
            { "MTLR", "BBG004S68598" },
            // добавить остальные тикеры по необходимости
        };

        // возвращает figi по тикеру
        public string? GetFigiByTicker(string ticker)
        {
            if (string.IsNullOrWhiteSpace(ticker))
                return null;

            _tickerToFigi.TryGetValue(ticker.Trim(), out var figi);
            return figi;
        }
    }
}
