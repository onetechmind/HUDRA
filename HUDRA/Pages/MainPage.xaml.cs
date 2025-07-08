using HUDRA.Controls;
using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace HUDRA.Pages
{
    public sealed partial class MainPage : UserControl
    {
        public event EventHandler? SettingsRequested;
        public MainPage()
        {
            this.InitializeComponent();
            SettingsButton.Click += OnSettingsButtonClick;
        }

        private void OnSettingsButtonClick(object sender, RoutedEventArgs e)
        {
            SettingsRequested?.Invoke(this, EventArgs.Empty);
        }

        public void Initialize(DpiScalingService dpiService,
                               ResolutionService resolutionService,
                               AudioService audioService,
                               BrightnessService brightnessService)
        {
            TdpPicker.Initialize(dpiService);
            ResolutionPicker.Initialize();
            AudioControls.Initialize();
            BrightnessControls.Initialize();

            ResolutionPicker.PropertyChanged += (s, e) =>
            {
                // Forwarded events can be handled by host if needed
            };
        }
    }
}


