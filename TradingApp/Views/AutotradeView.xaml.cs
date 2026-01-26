//AutotradeView.xaml.cs
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
using Trading.Core.Models;
using System.Threading;

namespace TradingApp
{
    public partial class AutotradeView : UserControl
    {
        private readonly TradeService _tradeService;
        private readonly SettingsService _settingsService;
        private CancellationTokenSource? _orderBookCts;
        private OrderBookScreenerService _screenerService;

        private TradingRuntime? _rt;
        private string _currentSymbol = "SBER@MISX";
        private OrderBookSnapshot? _lastSnap;

        // коллекции для DataGrid'ов
        private readonly ObservableCollection<OrderBookRow> _bids = new();
        private readonly ObservableCollection<OrderBookRow> _asks = new();

        private readonly string[] _tickers = { "SBER", "MTLR", "GAZP", "LKOH", "AFLT", "ASTR", "KROT", "X5", "BELU", "RUAL",
                                               "MOEX", "MGNT", "AFKS",  "ALRS", "HYDR",  "SVCB", "MTSS", "SIBN", "TATN", "MAGN"};

        private readonly System.Windows.Threading.DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };

        private readonly ObservableCollection<string> _logs = new();

        public AutotradeView()
        {
            InitializeComponent();

            TickerListBox.ItemsSource = _tickers;
            TickerListBox.SelectionChanged += TickerListBox_SelectionChanged;

            BidsGrid.ItemsSource = _bids;
            AsksGrid.ItemsSource = _asks;

            _uiTimer.Tick += (_, __) => RenderOrderBook();
            _uiTimer.Start();

            TickerListBox.SelectedIndex = 0;
            _currentSymbol = ToFinamSymbol((string)TickerListBox.SelectedItem!);

            LogsListBox.ItemsSource = _logs;
        }

        private void RenderOrderBook()
        {
            var snap = _lastSnap;
            if (snap == null) return;

            _bids.Clear();
            foreach (var b in snap.Bids)
                _bids.Add(new OrderBookRow { Price = (double)b.Price, Quantity = (long)b.Size });

            _asks.Clear();
            foreach (var a in snap.Asks)
                _asks.Add(new OrderBookRow { Price = (double)a.Price, Quantity = (long)a.Size });
        }

        public class OrderBookRow
        {
            public double Price { get; set; }
            public long Quantity { get; set; }
        }

        private void TickerListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TickerListBox.SelectedItem is string ticker)
                _currentSymbol = ToFinamSymbol(ticker);
                _lastSnap = null;
                _bids.Clear();
                _asks.Clear();
        }

        private static string ToFinamSymbol(string ticker) => $"{ticker}@MISX";

        public void SetRuntime(TradingRuntime rt)
        {
            _rt = rt;

            // подписываемся один раз
            _rt.MarketData.OrderBook += OnOrderBook;
            _rt.Trading.OrderUpdated += OnOrderUpdated;
            _rt.Trading.Trade += OnTrade;
        }

        private void OnOrderBook(OrderBookSnapshot snap)
        {
            if (snap.Symbol != _currentSymbol) return;
            _lastSnap = snap;
        }

        private void Log(string s)
        {
            // ограничим память, а то ты опять убьёшь оперативку, только уже логом
            Dispatcher.InvokeAsync(() =>
            {
                _logs.Insert(0, $"{DateTime.Now:HH:mm:ss} {s}");
                if (_logs.Count > 500) _logs.RemoveAt(_logs.Count - 1);
            });
        }

        private void OnOrderUpdated(OrderUpdate o) =>
            Log($"order {o.OrderId} {o.Symbol} {o.Side} {o.Status}");

        private void OnTrade(TradeUpdate t) =>
            Log($"trade {t.TradeId} {t.Symbol} {t.Side} {t.Price} x {t.Qty}");
    }
}