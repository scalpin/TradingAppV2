using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System;
using System.Text.Json;
using System.Net.Http;
using System.Threading.Tasks;
using TradingApp.ViewModels;
using System.Collections.ObjectModel;
using TradingApp.Services;
using Google.Api;
using static TradingApp.MainWindow;

namespace TradingApp
{
    public partial class AutotradeView : UserControl
    {
        private readonly TradeService _tradeService;
        private readonly SettingsService _settingsService;
        private CancellationTokenSource? _orderBookCts;
        private OrderBookScreenerService _screenerService;

        // коллекции для DataGrid'ов
        private readonly ObservableCollection<OrderBookRow> _bids = new();
        private readonly ObservableCollection<OrderBookRow> _asks = new();

        private readonly string[] _tickers = { "SBER", "MTLR", "GAZP", "LKOH", "AFLT", "ASTR", "KROT" };

        public AutotradeView()
        {
            InitializeComponent();

            _settingsService = new SettingsService();
            _tradeService = new TradeService(_settingsService);

            TickerListBox.ItemsSource = _tickers;
            TickerListBox.SelectionChanged += TickerListBox_SelectionChanged;

            BidsGrid.ItemsSource = _bids;
            AsksGrid.ItemsSource = _asks;

            // Выбираем первый тикер сразу
            TickerListBox.SelectedIndex = 0;

            // запустить стакан для первого выбранного
            StartOrderBookFor((string)TickerListBox.SelectedItem!);
        }

        public class OrderBookRow
        {
            public double Price { get; set; }
            public long Quantity { get; set; }
        }

        private void TickerListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TickerListBox.SelectedItem is string ticker)
                StartOrderBookFor(ticker);
        }

        private async void StartOrderBookFor(string ticker)
        {
            // отменяем предыдущую подписку
            _orderBookCts?.Cancel();
            _orderBookCts = new CancellationTokenSource();

            // получаем FIGI
            var figi = _settingsService.GetFigiByTicker(ticker);
            if (figi == null) return;

            // запускаем real‑time подписку
            await _tradeService.SubscribeOrderBookAsync(
                figi,
                depth: 20,
                onUpdate: ob =>
                {
                    // обновляем коллекции в UI‑потоке
                    Dispatcher.Invoke(() =>
                    {
                        _bids.Clear();
                        foreach (var b in ob.Bids)
                        {
                            _bids.Add(new OrderBookRow
                            {
                                Price = b.Price.Units + b.Price.Nano / 1e9,
                                Quantity = b.Quantity
                            });
                        }

                        _asks.Clear();
                        foreach (var a in ob.Asks)
                        {
                            _asks.Add(new OrderBookRow
                            {
                                Price = a.Price.Units + a.Price.Nano / 1e9,
                                Quantity = a.Quantity
                            });
                        }
                    });
                },
                cancellationToken: _orderBookCts.Token
            );
        }
    }
}