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
            { "GAZP", "BBG004730RP0" },
            { "LKOH", "BBG004731032" },
            { "MTLR", "BBG004S68598" },
        };
        // возвращает figi по тикеру
        public string? GetFigiByTicker(string ticker)
        {
            if (string.IsNullOrWhiteSpace(ticker))
                return null;

            _tickerToFigi.TryGetValue(ticker.Trim(), out var figi);
            return figi;
        }

        // Словарь «тикер -> размер лота»
        public static readonly Dictionary<string, int> LotSizes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["SBER"] = 10,
            ["GAZP"] = 10,
            ["LKOH"] = 1,
            ["MTLR"] = 1,
        };
        // возвращает размер лота по тикеру
        public int? GetLotSize(string ticker)
        {
            if (string.IsNullOrWhiteSpace(ticker))
                return 1;

            return LotSizes.TryGetValue(ticker.Trim(), out var size) ? size : 1;
        }
    }
}
