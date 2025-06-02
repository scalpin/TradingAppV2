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

            /*
            // заполнить ComboBox тикерами
            TickerComboBox.ItemsSource = new[] { "SBER", "MTLR", "GAZP", "LKOH", "AFLT", "ASTR", "KROT" };
            TickerComboBox.SelectedIndex = 0; // сразу выберет SBER

            // привязать DataGrid'ы к коллекциям
            BidsGrid.ItemsSource = _bids;
            AsksGrid.ItemsSource = _asks;

            // повесить обработчик выбора тикера
            TickerComboBox.SelectionChanged += TickerComboBox_SelectionChanged;

            // запустить стакан для первого выбранного
            StartOrderBookFor((string)TickerComboBox.SelectedItem!);
            */

            
        }

        public class OrderBookRow
        {
            public double Price { get; set; }
            public long Quantity { get; set; }
        }

        /*
        private void TickerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TickerComboBox.SelectedItem is string ticker)
                StartOrderBookFor(ticker);
        }
        */

        /*
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
        */

        
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            var settings = new SettingsService();

            /*
            // Запуск скриннера
            _screenerService = new OrderBookScreenerService(_tradeService.MarketDataStreamClient, settings, _tradeService);
            await _screenerService.StartAsync();
            */
            
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            /*
            // отменить подписку при закрытии
            _orderBookCts?.Cancel();
            */
            
        }
        

        /*
        private void ModeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AutotradePanel == null || ScreenerPanel == null)
            {
                return;
            }

            if (ModeSelector.SelectedIndex == 0)
            {
                AutotradePanel.Visibility = System.Windows.Visibility.Visible;
                ScreenerPanel.Visibility = System.Windows.Visibility.Collapsed;
            }
            else
            {
                AutotradePanel.Visibility = System.Windows.Visibility.Collapsed;
                ScreenerPanel.Visibility = System.Windows.Visibility.Visible;
            }
        }
        */
       

        
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