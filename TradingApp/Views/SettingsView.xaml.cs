using System.Windows;
using System.Windows.Controls;
using TradingApp.Services;

namespace TradingApp
{
    public partial class SettingsView : UserControl
    {
        private readonly SettingsService _settingsService;

        public SettingsView()
        {
            InitializeComponent();

            _settingsService = new SettingsService();
            this.DataContext = _settingsService.Settings; 
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            _settingsService.Save();
            MessageBox.Show("Настройки сохранены");
        }
    }
}