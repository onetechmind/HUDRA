using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace HUDRA.Pages
{
    public sealed partial class ShellPage : Page, INotifyPropertyChanged
    {
        private readonly INavigationService _navigationService;
        private readonly Dictionary<Type, string> _pageTitles = new();
        private DpiScalingService? _dpiService;
        private ResolutionService? _resolutionService;
        private AudioService? _audioService;
        private BrightnessService? _brightnessService;
        private MainPage? _mainPage;
        private TdpMonitorService? _tdpMonitor;
        public MainPage? MainPage => _mainPage;
        public SettingsPage? SettingsPage => ContentFrame.Content as SettingsPage;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ShellPage()
        {
            InitializeComponent();
            _navigationService = new NavigationService(ContentFrame);
            _navigationService.Navigated += OnNavigated;
            Loaded += ShellPage_Loaded;
            
            _pageTitles[typeof(MainPage)] = "HUDRA";
            _pageTitles[typeof(SettingsPage)] = "Settings";
        }

        public bool CanGoBack => _navigationService.CanGoBack;

        public Visibility BackButtonVisibility => CanGoBack ? Visibility.Visible : Visibility.Collapsed;

        private string _currentPageTitle = string.Empty;
        public string CurrentPageTitle
        {
            get => _currentPageTitle;
            set { _currentPageTitle = value; OnPropertyChanged(); }
        }

        private void ShellPage_Loaded(object sender, RoutedEventArgs e)
        {
            NavigateToPage<MainPage>();
        }


        public void InitializeServices(DpiScalingService dpi,
                                       ResolutionService resolution,
                                       AudioService audio,
                                       BrightnessService brightness)
        {
            _dpiService = dpi;
            _resolutionService = resolution;
            _audioService = audio;
            _brightnessService = brightness;
        }

        public void SetTdpMonitor(TdpMonitorService monitor)
        {
            _tdpMonitor = monitor;
            if (_mainPage != null)
            {
                SetupTdpMonitor();
            }
        }

        public void NavigateToPage<T>(object? parameter = null)
        {
            _navigationService.Navigate<T>(parameter);
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            _navigationService.GoBack();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            NavigateToPage<SettingsPage>();
        }

        private void AltTabButton_Click(object sender, RoutedEventArgs e)
        {
            SimulateAltTab();
        }

        private void HideButton_Click(object sender, RoutedEventArgs e)
        {
            (App.Current.MainWindow as MainWindow)?.ToggleWindowVisibility();
        }

        private void OnNavigated(object? sender, NavigationEventArgs e)
        {
            CurrentPageTitle = _pageTitles.TryGetValue(e.SourcePageType, out var title) ? title : string.Empty;
            OnPropertyChanged(nameof(BackButtonVisibility));

            if (ContentFrame.Content is MainPage mp && _dpiService != null)
            {
                mp.Initialize(_dpiService, _resolutionService!, _audioService!, _brightnessService!);
                _mainPage = mp;
                SetupTdpMonitor();
            }
            else if (ContentFrame.Content is SettingsPage sp && _dpiService != null)
            {
                sp.Initialize(_dpiService);
                _mainPage = null;
            }
        }

        private void SetupTdpMonitor()
        {
            if (_tdpMonitor == null || _mainPage == null) return;

            bool started = false;

            _mainPage.TdpPicker.TdpChanged += (s, value) =>
            {
                _tdpMonitor.UpdateTargetTdp(value);
                if (!started && SettingsService.GetTdpCorrectionEnabled() && value > 0)
                {
                    _tdpMonitor.Start();
                    started = true;
                }
            };

            if (_mainPage.TdpPicker.SelectedTdp > 0)
            {
                _tdpMonitor.UpdateTargetTdp(_mainPage.TdpPicker.SelectedTdp);
                if (SettingsService.GetTdpCorrectionEnabled())
                {
                    _tdpMonitor.Start();
                    started = true;
                }
            }

            _tdpMonitor.TdpDriftDetected += (s, args) =>
            {
                System.Diagnostics.Debug.WriteLine($"TDP drift {args.CurrentTdp}W -> {args.TargetTdp}W (corrected: {args.CorrectionApplied})");
            };
        }

        private void SimulateAltTab()
        {
            keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
            keybd_event(VK_TAB, 0, 0, UIntPtr.Zero);
            keybd_event(VK_TAB, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const byte VK_MENU = 0x12;
        private const byte VK_TAB = 0x09;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
