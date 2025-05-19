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
            // если уже была подписка — отменяем
            _orderBookCts?.Cancel();
            _orderBookCts = new CancellationTokenSource();

            await _tradeService.SubscribeOrderBookAsync(
                figi: "BBG004730N88",
                depth: 10,
                onUpdate: ob =>
                {
                    // Этот код выполняется в потоке gRPC-обработчика,
                    // если нужно обновить UI — надо через Dispatcher
                    var sb = new StringBuilder();
                    sb.AppendLine("=== BIDS ===");
                    foreach (var b in ob.Bids)
                    {
                        var price = b.Price.Units + b.Price.Nano / 1_000_000_000.0;
                        sb.AppendLine($"Bid: {price} x {b.Quantity}");
                    }
                    sb.AppendLine("=== ASKS ===");
                    foreach (var a in ob.Asks)
                    {
                        var price = a.Price.Units + a.Price.Nano / 1_000_000_000.0;
                        sb.AppendLine($"Ask: {price} x {a.Quantity}");
                    }
                    System.Diagnostics.Debug.WriteLine(sb.ToString());
                },
                cancellationToken: _orderBookCts.Token
            );
        }

        // остановка подписки
        private void StopOrderBook()
        {
            _orderBookCts?.Cancel();
            _orderBookCts = null;
        }
    }
}