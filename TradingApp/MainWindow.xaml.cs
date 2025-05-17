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

namespace TradingApp
{
    public partial class MainWindow : Window
    {
        private readonly OrdersViewModel _ordersViewModel;

        public MainWindow()
        {
            InitializeComponent();
            _ordersViewModel = new OrdersViewModel();
            DataContext = _ordersViewModel; // связываем с ViewModel
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            // Мы отправляем запрос на создание заявки
            await _ordersViewModel.PlaceTestOrder();
        }

        private async void ShowOrderBookCommand_Click(object sender, RoutedEventArgs e)
        {
            await _ordersViewModel.GetOrderBookAsync();
        }

    }
}