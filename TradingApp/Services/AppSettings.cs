using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradingApp.Services
{
    public class AppSettings : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private string _fToken = "";
        public string fToken
        {
            get => _fToken;
            set
            {
                if (_fToken != value)
                {
                    _fToken = value;
                    OnPropertyChanged(nameof(fToken));
                }
            }
        }

        private string _tToken = "";
        public string tToken
        {
            get => _tToken;
            set
            {
                if (_tToken != value)
                {
                    _tToken = value;
                    OnPropertyChanged(nameof(tToken));
                }
            }
        }

        private string _clientId = "";
        public string ClientId
        {
            get => _clientId;
            set
            {
                if (_clientId != value)
                {
                    _clientId = value;
                    OnPropertyChanged(nameof(ClientId));
                }
            }
        }

        private void OnPropertyChanged(string propName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }
}
