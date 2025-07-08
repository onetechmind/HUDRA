using HUDRA.Controls;
using HUDRA.Services;
using Microsoft.UI.Xaml.Controls;

namespace HUDRA.Pages
{
    public sealed partial class MainPage : UserControl
    {
        public MainPage()
        {
            this.InitializeComponent();
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


