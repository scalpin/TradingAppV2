using System;
using System.Windows;
using System.Windows.Input;
using System.Threading.Tasks;
using TradingApp.Services;
using TradingApp.Helpers;
using System.Text;
using Tinkoff.InvestApi.V1;

namespace TradingApp.ViewModels
{
    public class OrdersViewModel
    {
        private readonly TradeService _tradeService;

        public ICommand ShowOrderBookCommand { get; }

        public ICommand PlaceOrderCommand { get; }

        public OrdersViewModel()
        {
            var settings = new SettingsService();
            _tradeService = new TradeService(settings);

            PlaceOrderCommand = new RelayCommand(async _ => await PlaceTestOrder());
        }

        public async Task PlaceTestOrder()
        {
            await _tradeService.PlaceTakeProfitOrderAsync("TQBR", "MTLR", 500);
        }

        public async Task GetOrderBookAsync()
        {
            await _tradeService.GetOrderBookAsync("BBG004730N88");
        }

    }
}