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
            { "AFLT", "BBG004S683W7" },
            { "ASTR", "BBG01JRXN2X9" },
            { "KROT", "BBG000NLB2G3" },
            { "X5",   "BBG00JXPFBN0" },
            { "BELU", "BBG000TY1C41" },
            { "RUAL", "BBG008F2T3T2" },
            { "MOEX", "BBG004730JJ5" },
            { "MGNT", "BBG004RVFCY3" },
            { "AFKS", "BBG004S68614" },
            { "ALRS", "BBG004S68B31" },
            { "HYDR", "BBG00475K2X9" },
            { "SVCB", "BBG000FR4JW2" },
            { "MTSS", "BBG004S681W1" },
            { "SIBN", "BBG004S684M6" },
        };
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
            ["AFLT"] = 10,
            ["ASTR"] = 1,
            ["X5"] = 1,
            ["BELU"] = 1,
            ["KROT"] = 10,
            ["RUAL"] = 10,
            ["MOEX"] = 10,
            ["MGNT"] = 1,
            ["AFKS"] = 100,
            ["ALRS"] = 10,
            ["HYDR"] = 1000,
            ["SVCB"] = 100,
            ["MTSS"] = 10,
            ["SIBN"]= 1,
        };
        public int? GetLotSize(string ticker)
        {
            if (string.IsNullOrWhiteSpace(ticker))
                return 1;

            return LotSizes.TryGetValue(ticker.Trim(), out var size) ? size : 1;
        }

        // Словарь "тикер -> шаг цены"
        public static readonly Dictionary<string, double> TickSizes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["SBER"] = 0.01,
            ["GAZP"] = 0.01,
            ["LKOH"] = 0.5,
            ["MTLR"] = 0.01,
            ["AFLT"] = 0.01,
            ["ASTR"] = 0.01,
            ["X5"]   = 0.5,
            ["BELU"] = 0.5,
            ["KROT"] = 1.0,
            ["RUAL"] = 0.05,
            ["MOEX"] = 0.01,
            ["MGNT"] = 0.5,
            ["AFKS"] = 0.001,
            ["ALRS"] = 0.01,
            ["HYDR"] = 0.0001,
            ["SVCB"] = 0.005,
            ["MTSS"] = 0.05,
            ["SIBN"] = 0.05,
        };
        public double GetTickSize(string ticker)
        {
            if (string.IsNullOrWhiteSpace(ticker))
                return 0.01;

            return TickSizes.TryGetValue(ticker.Trim(), out var tickSize) ? tickSize : 0.01;
        }
    }
}
