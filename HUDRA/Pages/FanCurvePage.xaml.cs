using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;

namespace HUDRA.Pages
{
    public sealed partial class FanCurvePage : Page
    {
        private bool _isInitialized = false;

        public FanCurvePage()
        {
            this.InitializeComponent();
        }

        public void Initialize()
        {
            System.Diagnostics.Debug.WriteLine("=== FanCurvePage Initialize called ===");
            
            try
            {
                // Initialize the Fan Curve Control
                FanCurveControl.Initialize();
                SetupFanCurveEventHandling();
                
                _isInitialized = true;
                System.Diagnostics.Debug.WriteLine("=== FanCurvePage initialization complete ===");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR in FanCurvePage Initialize: {ex.Message}");
            }
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            System.Diagnostics.Debug.WriteLine("=== FanCurvePage OnNavigatedTo ===");
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            System.Diagnostics.Debug.WriteLine("=== FanCurvePage OnNavigatedFrom ===");
            
            // Cleanup if needed
            if (_isInitialized)
            {
                // Add any cleanup logic here if needed
                _isInitialized = false;
            }
        }

        private void SetupFanCurveEventHandling()
        {
            // Handle fan curve control events
            FanCurveControl.FanCurveChanged += (s, e) =>
            {
                if (e.Curve.IsEnabled)
                {
                    System.Diagnostics.Debug.WriteLine("Fan curve control active - custom curve applied");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Fan curve control disabled - hardware mode active");
                }
                
                // Could add additional logging or status updates here
                // e.g., update main window status, log to file, etc.
            };
        }

    }
}