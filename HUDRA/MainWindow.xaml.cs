using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace HUDRA
{
    public sealed partial class MainWindow : Window, INotifyPropertyChanged
    {

        // This is the event the UI system will listen for.
        public event PropertyChangedEventHandler? PropertyChanged;

        // This is a helper method to raise the event.
        // The [CallerMemberName] attribute automatically fills in the name of the property that called it.
        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string _currentTdpDisplayText = "Current TDP: Not Set";
        public string CurrentTdpDisplayText
        {
            get => _currentTdpDisplayText;
            set
            {
                if (_currentTdpDisplayText != value)
                {
                    _currentTdpDisplayText = value;
                    OnPropertyChanged(); // This tells the UI to update itself!
                }
            }
        }

        public MainWindow()
        {
            this.InitializeComponent();
            this.Title = "HUDRA Control Center";
            LayoutRoot.DataContext = this;
        }

        private async void SetTdpButton_Click(object sender, RoutedEventArgs e)
        {
            // 1. Create an instance of your new service
            var tdpService = new TDPService();

            // 2. Get the value from the UI and convert watts to milliwatts
            int targetTdp = (int)TdpSlider.Value;
            int tdpInMilliwatts = targetTdp * 1000;

            // 3. Call the service and capture the result tuple
            var result = tdpService.SetTdp(tdpInMilliwatts);

            // 4. Update display text to show new TDP
            if (result.Success)
            {
                CurrentTdpDisplayText = $"Current TDP: {targetTdp}W";
            }
            else
            {
                // You can also use this to display errors!
                CurrentTdpDisplayText = $"Error: {result.Message}";
            }
        }
    }
}
