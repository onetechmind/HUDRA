using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HUDRA.Controls
{
    public class TdpItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool _isSelected;
        private double _fontSize = 24;
        private double _opacity = 0.4;

        public int Value { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    UpdateVisualProperties();
                    OnPropertyChanged();
                }
            }
        }

        public double FontSize
        {
            get => _fontSize;
            private set
            {
                if (Math.Abs(_fontSize - value) > 0.1)
                {
                    _fontSize = value;
                    OnPropertyChanged();
                }
            }
        }

        public double Opacity
        {
            get => _opacity;
            private set
            {
                if (Math.Abs(_opacity - value) > 0.01)
                {
                    _opacity = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DisplayText => Value.ToString();

        public TdpItem(int value)
        {
            Value = value;
        }

        private void UpdateVisualProperties()
        {
            if (_isSelected)
            {
                FontSize = 28;
                Opacity = 1.0;
            }
            else
            {
                FontSize = 24;
                Opacity = 0.4;
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}