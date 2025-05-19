using System;
using System.Windows;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Text;
using TradingApp.Services;
using TradingApp.Helpers;
using Tinkoff.InvestApi.V1;

namespace TradingApp.ViewModels
{
    public class OrdersViewModel
    {
        private readonly TradeService _tradeService;
        private CancellationTokenSource? _orderBookCts;

        public ICommand ShowOrderBookCommand { get; }

        public ICommand PlaceOrderCommand { get; }

        public ICommand StartOrderBookCommand { get; }
        public ICommand StopOrderBookCommand { get; }

        public OrdersViewModel()
        {
            var settings = new SettingsService();
            _tradeService = new TradeService(settings);

            PlaceOrderCommand = new RelayCommand(async _ => await PlaceTestOrder());


            StartOrderBookCommand = new RelayCommand(async _ => await StartOrderBook());
            StopOrderBookCommand = new RelayCommand(async _ => StopOrderBook());
        }

        // Тейкпрофит
        public async Task PlaceTestOrder()
        {
            await _tradeService.PlaceTakeProfitOrderAsync("TQBR", "MTLR", 500);
        }

        // фотка стакана
        public async Task GetOrderBookAsync()
        {
            await _tradeService.GetOrderBookAsync("BBG004730N88");
        }

        // подписка на стакан
        public async Task StartOrderBook()
        {
            _orderBookCts?.Cancel();
            _orderBookCts = new CancellationTokenSource();
            await _tradeService.SubscribeAndLogOrderBookAsync(
                figi: "BBG004730N88",
                depth: 10,
                cancellationToken: _orderBookCts.Token);
        }

        // остановка подписки
        private void StopOrderBook()
        {
            _orderBookCts?.Cancel();
            _orderBookCts = null;
        }
    }
}