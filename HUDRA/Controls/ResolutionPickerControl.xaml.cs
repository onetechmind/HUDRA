using HUDRA.Configuration;
using HUDRA.Helpers;
using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace HUDRA.Controls
{
    public sealed partial class ResolutionPickerControl : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event EventHandler<ResolutionChangedEventArgs>? ResolutionChanged;
        public event EventHandler<RefreshRateChangedEventArgs>? RefreshRateChanged;

        private ResolutionService? _resolutionService;
        private ResolutionAutoSetManager? _resolutionAutoSetManager;
        private RefreshRateAutoSetManager? _refreshRateAutoSetManager;

        private List<ResolutionService.Resolution> _availableResolutions = new();
        private List<int> _availableRefreshRates = new();
        private int _selectedResolutionIndex = 0;
        private int _selectedRefreshRateIndex = 0;

        private string _resolutionStatusText = "Resolution: Not Set";
        public string ResolutionStatusText
        {
            get => _resolutionStatusText;
            set
            {
                if (_resolutionStatusText != value)
                {
                    _resolutionStatusText = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _refreshRateStatusText = "Refresh Rate: Not Set";
        public string RefreshRateStatusText
        {
            get => _refreshRateStatusText;
            set
            {
                if (_refreshRateStatusText != value)
                {
                    _refreshRateStatusText = value;
                    OnPropertyChanged();
                }
            }
        }


        public ResolutionPickerControl()
        {
            this.InitializeComponent();
        }

        public void Initialize()
        {
            _resolutionService = new ResolutionService();

            _resolutionAutoSetManager = new ResolutionAutoSetManager(SetResolutionAsync, status => ResolutionStatusText = status);
            _refreshRateAutoSetManager = new RefreshRateAutoSetManager(SetRefreshRateAsync, status => RefreshRateStatusText = status);

            InitializeResolutions();
            SetupEventHandlers();
            LoadCurrentSettings();
        }

        private void InitializeResolutions()
        {
            if (_resolutionService == null) return;

            _availableResolutions = _resolutionService.GetAvailableResolutions();

            if (_availableResolutions.Count == 0)
            {
                ResolutionStatusText = "No resolutions available";
                if (ResolutionComboBox != null)
                    ResolutionComboBox.IsEnabled = false;
                return;
            }

            if (ResolutionComboBox != null)
            {
                ResolutionComboBox.ItemsSource = _availableResolutions.Select(r => r.DisplayText).ToList();
                ResolutionComboBox.IsEnabled = true;
            }

            // Initialize with first resolution
            if (_availableResolutions.Count > 0)
            {
                UpdateRefreshRatesForResolution(0);
            }
        }

        private void SetupEventHandlers()
        {
            if (ResolutionComboBox != null)
            {
                ResolutionComboBox.SelectionChanged += OnResolutionSelectionChanged;
                ResolutionComboBox.DropDownOpened += (s, e) => { /* Can add popup handling later */ };
                ResolutionComboBox.DropDownClosed += (s, e) => { /* Can add popup handling later */ };
            }

            if (RefreshRateComboBox != null)
            {
                RefreshRateComboBox.SelectionChanged += OnRefreshRateSelectionChanged;
                RefreshRateComboBox.DropDownOpened += (s, e) => { /* Can add popup handling later */ };
                RefreshRateComboBox.DropDownClosed += (s, e) => { /* Can add popup handling later */ };
            }
        }

        private void OnResolutionSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ResolutionComboBox?.SelectedIndex < 0 ||
                ResolutionComboBox.SelectedIndex >= _availableResolutions.Count ||
                _resolutionAutoSetManager == null)
                return;

            _selectedResolutionIndex = ResolutionComboBox.SelectedIndex;
            UpdateRefreshRatesForResolution(_selectedResolutionIndex);

            var selectedResolution = _availableResolutions[_selectedResolutionIndex];
            ResolutionChanged?.Invoke(this, new ResolutionChangedEventArgs(selectedResolution, false));

            _resolutionAutoSetManager.ScheduleUpdate(_selectedResolutionIndex);
        }

        private void OnRefreshRateSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RefreshRateComboBox?.SelectedIndex < 0 ||
                RefreshRateComboBox.SelectedIndex >= _availableRefreshRates.Count ||
                _refreshRateAutoSetManager == null)
                return;

            _selectedRefreshRateIndex = RefreshRateComboBox.SelectedIndex;

            var selectedRefreshRate = _availableRefreshRates[_selectedRefreshRateIndex];
            RefreshRateChanged?.Invoke(this, new RefreshRateChangedEventArgs(selectedRefreshRate, false));

            _refreshRateAutoSetManager.ScheduleUpdate(_selectedRefreshRateIndex);
        }

        private void UpdateRefreshRatesForResolution(int resolutionIndex)
        {
            if (_resolutionService == null || resolutionIndex < 0 || resolutionIndex >= _availableResolutions.Count)
                return;

            // Remember the current refresh rate value (not index)
            int currentRefreshRateValue = 0;
            if (_availableRefreshRates.Count > 0 && _selectedRefreshRateIndex >= 0 && _selectedRefreshRateIndex < _availableRefreshRates.Count)
            {
                currentRefreshRateValue = _availableRefreshRates[_selectedRefreshRateIndex];
            }

            var selectedResolution = _availableResolutions[resolutionIndex];
            _availableRefreshRates = _resolutionService.GetAvailableRefreshRates(selectedResolution);

            if (RefreshRateComboBox != null)
            {
                RefreshRateComboBox.ItemsSource = _availableRefreshRates.Select(rate => $"{rate}Hz").ToList();

                if (_availableRefreshRates.Count > 0)
                {
                    // Try to find the previous refresh rate in the new list
                    int newRefreshRateIndex = -1;
                    if (currentRefreshRateValue > 0)
                    {
                        newRefreshRateIndex = _availableRefreshRates.FindIndex(rate => rate == currentRefreshRateValue);
                    }

                    // If previous refresh rate is available, use it; otherwise use the highest available
                    if (newRefreshRateIndex >= 0)
                    {
                        _selectedRefreshRateIndex = newRefreshRateIndex;
                        RefreshRateComboBox.SelectedIndex = newRefreshRateIndex;
                        RefreshRateStatusText = $"Refresh Rate: {currentRefreshRateValue}Hz (preserved)";
                    }
                    else
                    {
                        // Fall back to the highest refresh rate available
                        var highestRefreshRate = _availableRefreshRates.Max();
                        var highestIndex = _availableRefreshRates.FindIndex(rate => rate == highestRefreshRate);

                        _selectedRefreshRateIndex = highestIndex >= 0 ? highestIndex : 0;
                        RefreshRateComboBox.SelectedIndex = _selectedRefreshRateIndex;

                        if (currentRefreshRateValue > 0)
                        {
                            RefreshRateStatusText = $"Refresh Rate: {_availableRefreshRates[_selectedRefreshRateIndex]}Hz (adjusted from {currentRefreshRateValue}Hz)";
                        }
                        else
                        {
                            RefreshRateStatusText = $"Refresh Rate: {_availableRefreshRates[_selectedRefreshRateIndex]}Hz";
                        }
                    }

                    RefreshRateComboBox.IsEnabled = true;
                }
                else
                {
                    RefreshRateComboBox.IsEnabled = false;
                    RefreshRateStatusText = "No refresh rates available";
                }
            }
        }

        private void LoadCurrentSettings()
        {
            if (_resolutionService == null) return;

            try
            {
                // Load current resolution
                var currentRes = _resolutionService.GetCurrentResolution();
                if (currentRes.Success)
                {
                    var match = _availableResolutions.FindIndex(r =>
                        r.Width == currentRes.CurrentResolution.Width &&
                        r.Height == currentRes.CurrentResolution.Height);

                    if (match >= 0)
                    {
                        _selectedResolutionIndex = match;
                        if (ResolutionComboBox != null)
                            ResolutionComboBox.SelectedIndex = match;
                        UpdateRefreshRatesForResolution(match);
                    }

                    ResolutionStatusText = $"Resolution: {currentRes.CurrentResolution.DisplayText}";
                }
                else
                {
                    ResolutionStatusText = $"Resolution Error: {currentRes.Message}";
                }

                // Load current refresh rate
                var currentRefreshRate = _resolutionService.GetCurrentRefreshRate();
                if (currentRefreshRate.Success)
                {
                    var refreshMatch = _availableRefreshRates.FindIndex(rate => rate == currentRefreshRate.RefreshRate);
                    if (refreshMatch >= 0)
                    {
                        _selectedRefreshRateIndex = refreshMatch;
                        if (RefreshRateComboBox != null)
                            RefreshRateComboBox.SelectedIndex = refreshMatch;
                    }

                    RefreshRateStatusText = $"Refresh Rate: {currentRefreshRate.RefreshRate}Hz";
                }
                else
                {
                    RefreshRateStatusText = $"Refresh Rate Error: {currentRefreshRate.Message}";
                }
            }
            catch (Exception ex)
            {
                ResolutionStatusText = $"Error loading settings: {ex.Message}";
            }
        }

        private async Task<bool> SetResolutionAsync(int resolutionIndex)
        {
            try
            {
                if (_resolutionService == null || resolutionIndex < 0 || resolutionIndex >= _availableResolutions.Count)
                    return false;

                var targetResolution = _availableResolutions[resolutionIndex];

                // Use current refresh rate if available
                int refreshRate = targetResolution.RefreshRate;
                if (_availableRefreshRates != null &&
                    _selectedRefreshRateIndex >= 0 &&
                    _selectedRefreshRateIndex < _availableRefreshRates.Count)
                {
                    refreshRate = _availableRefreshRates[_selectedRefreshRateIndex];
                }

                var result = _resolutionService.SetRefreshRate(targetResolution, refreshRate);

                ResolutionStatusText = result.Success
                    ? $"Resolution: {targetResolution.DisplayText}"
                    : $"Resolution Error: {result.Message}";

                if (result.Success)
                {
                    ResolutionChanged?.Invoke(this, new ResolutionChangedEventArgs(targetResolution, true));
                }

                return result.Success;
            }
            catch (Exception ex)
            {
                ResolutionStatusText = $"Resolution Error: {ex.Message}";
                return false;
            }
        }

        private async Task<bool> SetRefreshRateAsync(int refreshRateIndex)
        {
            try
            {
                if (_resolutionService == null || refreshRateIndex < 0 || refreshRateIndex >= _availableRefreshRates.Count ||
                    _selectedResolutionIndex < 0 || _selectedResolutionIndex >= _availableResolutions.Count)
                    return false;

                var targetRefreshRate = _availableRefreshRates[refreshRateIndex];
                var currentResolution = _availableResolutions[_selectedResolutionIndex];

                var result = _resolutionService.SetRefreshRate(currentResolution, targetRefreshRate);

                RefreshRateStatusText = result.Success
                    ? $"Refresh Rate: {targetRefreshRate}Hz"
                    : $"Refresh Rate Error: {result.Message}";

                if (result.Success)
                {
                    RefreshRateChanged?.Invoke(this, new RefreshRateChangedEventArgs(targetRefreshRate, true));
                }

                return result.Success;
            }
            catch (Exception ex)
            {
                RefreshRateStatusText = $"Refresh Rate Error: {ex.Message}";
                return false;
            }
        }


        public void Dispose()
        {
            _resolutionAutoSetManager?.Dispose();
            _refreshRateAutoSetManager?.Dispose();
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Event argument classes
    public class ResolutionChangedEventArgs : EventArgs
    {
        public ResolutionService.Resolution Resolution { get; }
        public bool IsApplied { get; }

        public ResolutionChangedEventArgs(ResolutionService.Resolution resolution, bool isApplied)
        {
            Resolution = resolution;
            IsApplied = isApplied;
        }
    }

    public class RefreshRateChangedEventArgs : EventArgs
    {
        public int RefreshRate { get; }
        public bool IsApplied { get; }

        public RefreshRateChangedEventArgs(int refreshRate, bool isApplied)
        {
            RefreshRate = refreshRate;
            IsApplied = isApplied;
        }
    }
}