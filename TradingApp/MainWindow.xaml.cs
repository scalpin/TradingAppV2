//MainWindow.xaml.cs
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

namespace TradingApp
{
    public partial class MainWindow : Window
    {
        private string currentView = "Autotrade"; // По умолчанию "Автоторговля"

        private readonly TradeService _tradeService;
        private readonly SettingsService _settingsService;
        private CancellationTokenSource? _orderBookCts;
        private OrderBookScreenerService _screenerService;

        private TradingRuntime? _runtime;

        // коллекции для DataGrid'ов
        private readonly ObservableCollection<OrderBookRow> _bids = new();
        private readonly ObservableCollection<OrderBookRow> _asks = new();

        public MainWindow()
        {
            InitializeComponent();
            
            
            // инициализируем TradeService
            var settings = new SettingsService();
            _tradeService = new TradeService(settings);
            _settingsService = new SettingsService();

        }

        public class OrderBookRow
        {
            public double Price { get; set; }
            public long Quantity { get; set; }
        }


        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var secret = Environment.GetEnvironmentVariable("FINAM_SECRET");
            if (string.IsNullOrWhiteSpace(secret))
            {
                // можешь вывести в лог/MessageBox
                return;
            }

            _runtime = new TradingRuntime(secret);

            // пока только один символ, чтобы увидеть в UI
            // позже расширишь список или сделаешь динамическую подписку
            var tickers = new[] { "SBER", "MTLR", "GAZP", "LKOH", "AFLT", "ASTR", "KROT", "X5", "BELU", "RUAL",
                      "MOEX", "MGNT", "AFKS", "ALRS", "HYDR", "SVCB", "MTSS", "SIBN", "TATN", "MAGN" };

            var symbols = tickers.Select(t => $"{t}@MISX").ToArray();

            await _runtime.StartAsync(symbols, s => System.Diagnostics.Debug.WriteLine(s));

            // пробросим runtime во вьюхи
            AutotradeView.SetRuntime(_runtime);
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _runtime?.Dispose();
        }


        public void BtnAuto_Click(object sender, RoutedEventArgs e)
        {
            BtnAuto.IsChecked = true;
            BtnScreener.IsChecked = false;

            AutotradeView.Visibility = System.Windows.Visibility.Visible;
            SettingsView.Visibility = System.Windows.Visibility.Collapsed;
            ScreenerView.Visibility = System.Windows.Visibility.Collapsed;
            SwitcherPanel.Visibility = System.Windows.Visibility.Visible;
            currentView = "Autotrade";
        }

        private void BtnScreener_Click(object sender, RoutedEventArgs e)
        {
            BtnAuto.IsChecked = false;
            BtnScreener.IsChecked = true;

            AutotradeView.Visibility = System.Windows.Visibility.Collapsed;
            SettingsView.Visibility = System.Windows.Visibility.Collapsed;
            ScreenerView.Visibility = System.Windows.Visibility.Visible;
            SwitcherPanel.Visibility = System.Windows.Visibility.Visible;
            currentView = "Screener";
        }

        private void SettingsIcon_MouseLeftButtonDown(object sender, RoutedEventArgs e)
        {
            AutotradeView.Visibility = System.Windows.Visibility.Collapsed;
            ScreenerView.Visibility = System.Windows.Visibility.Collapsed;
            SettingsView.Visibility = System.Windows.Visibility.Visible;

            MainHeaderPanel.Visibility = System.Windows.Visibility.Collapsed;
            SettingsHeaderPanel.Visibility = System.Windows.Visibility.Visible;
        }

        public void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (currentView == "Autotrade")
            {
                AutotradeView.Visibility = System.Windows.Visibility.Visible;
                ScreenerView.Visibility = System.Windows.Visibility.Collapsed;
            }
            else if (currentView == "Screener")
            {
                AutotradeView.Visibility = System.Windows.Visibility.Collapsed;
                ScreenerView.Visibility = System.Windows.Visibility.Visible;
            }
            SettingsView.Visibility = System.Windows.Visibility.Collapsed;

            MainHeaderPanel.Visibility = System.Windows.Visibility.Visible;
            SettingsHeaderPanel.Visibility = System.Windows.Visibility.Collapsed;
        }
    }
}