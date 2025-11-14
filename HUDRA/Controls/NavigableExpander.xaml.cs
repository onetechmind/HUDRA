using HUDRA.AttachedProperties;
using HUDRA.Interfaces;
using HUDRA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HUDRA.Controls
{
    public sealed partial class NavigableExpander : UserControl, IGamepadNavigable, INotifyPropertyChanged, IDisposable
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private GamepadNavigationService? _gamepadNavigationService;
        private bool _isFocused = false;

        public NavigableExpander()
        {
            this.InitializeComponent();
            InitializeGamepadNavigation();
            this.Loaded += NavigableExpander_Loaded;
        }

        private void NavigableExpander_Loaded(object sender, RoutedEventArgs e)
        {
            // Ensure content visibility is set correctly when loaded
            UpdateContentVisibility();
        }

        private void UpdateContentVisibility()
        {
            if (BodyContentPresenter != null)
            {
                BodyContentPresenter.Visibility = IsExpanded ? Visibility.Visible : Visibility.Collapsed;
                System.Diagnostics.Debug.WriteLine($"🎮 NavigableExpander: Set content visibility to {BodyContentPresenter.Visibility} (IsExpanded: {IsExpanded})");
            }
        }

        // Header DP
        public static readonly DependencyProperty HeaderProperty =
            DependencyProperty.Register(nameof(Header), typeof(object), typeof(NavigableExpander), new PropertyMetadata(null));
        public object? Header
        {
            get => GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }

        // IsExpanded DP
        public static readonly DependencyProperty IsExpandedProperty =
            DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(NavigableExpander), new PropertyMetadata(false, OnIsExpandedChanged));
        public bool IsExpanded
        {
            get => (bool)GetValue(IsExpandedProperty);
            set => SetValue(IsExpandedProperty, value);
        }

        private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is NavigableExpander expander)
            {
                expander.UpdateContentVisibility();
            }
        }

        // Focus visuals
        public Brush FocusBorderBrush => (IsFocused && _gamepadNavigationService?.IsGamepadActive == true)
            ? new SolidColorBrush(Microsoft.UI.Colors.DarkViolet)
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        public Thickness FocusBorderThickness => (IsFocused && _gamepadNavigationService?.IsGamepadActive == true)
            ? new Thickness(2)
            : new Thickness(0);

        // Body DP to host page-provided content
        public static readonly DependencyProperty BodyProperty =
            DependencyProperty.Register(nameof(Body), typeof(object), typeof(NavigableExpander), new PropertyMetadata(null));
        public object? Body
        {
            get => GetValue(BodyProperty);
            set => SetValue(BodyProperty, value);
        }

        private void InitializeGamepadNavigation()
        {
            GamepadNavigation.SetIsEnabled(this, true);
            GamepadNavigation.SetNavigationGroup(this, "MainControls");
            GamepadNavigation.SetNavigationOrder(this, 1);
        }

        private void InitializeGamepadNavigationService()
        {
            if (_gamepadNavigationService != null) return;
            if (Application.Current is App app && app.MainWindow is MainWindow mainWindow)
            {
                _gamepadNavigationService = mainWindow.GamepadNavigationService;
                if (_gamepadNavigationService != null)
                {
                    _gamepadNavigationService.GamepadActiveStateChanged += OnGamepadActiveStateChanged;
                }
            }
        }

        private void OnGamepadActiveStateChanged(object? sender, bool isActive)
        {
            OnPropertyChanged(nameof(FocusBorderBrush));
            OnPropertyChanged(nameof(FocusBorderThickness));
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // IGamepadNavigable
        public bool CanNavigateUp => false;
        public bool CanNavigateDown => IsExpanded && Body is IGamepadNavigable;
        public bool CanNavigateLeft => false;
        public bool CanNavigateRight => false;
        public bool CanActivate => true;

        public FrameworkElement NavigationElement => this;

        public bool IsSlider => false;
        public bool IsSliderActivated { get; set; } = false;
        public void AdjustSliderValue(int direction) { }

        public bool HasComboBoxes => false;
        public bool IsComboBoxOpen { get; set; } = false;
        public ComboBox? GetFocusedComboBox() => null;
        public int ComboBoxOriginalIndex { get; set; } = -1;
        public bool IsNavigatingComboBox { get; set; } = false;
        public void ProcessCurrentSelection() { }

        public void OnGamepadNavigateUp() { }
        public void OnGamepadNavigateDown()
        {
            // Navigate into body content if expanded and it's navigable
            if (IsExpanded && Body is IGamepadNavigable navigable)
            {
                // Transfer focus to the body control
                if (_gamepadNavigationService != null && Body is FrameworkElement bodyElement)
                {
                    _gamepadNavigationService.SetFocus(bodyElement);
                    System.Diagnostics.Debug.WriteLine($"🎮 NavigableExpander: Navigated DOWN into body content");
                }
            }
        }
        public void OnGamepadNavigateLeft() { }
        public void OnGamepadNavigateRight() { }

        public void OnGamepadActivate()
        {
            // Toggle expander
            IsExpanded = !IsExpanded;
            if (InnerExpander != null)
            {
                InnerExpander.IsExpanded = IsExpanded;
            }
        }

        public void OnGamepadFocusReceived()
        {
            if (_gamepadNavigationService == null)
            {
                InitializeGamepadNavigationService();
            }
            IsFocused = true;
        }

        public void OnGamepadFocusLost()
        {
            IsFocused = false;
        }

        public void FocusLastElement()
        {
            // NavigableExpander is just a header, no internal elements to focus
            // This method is not applicable for expanders
        }

        public bool IsFocused
        {
            get => _isFocused;
            set
            {
                if (_isFocused != value)
                {
                    _isFocused = value;
                    OnPropertyChanged(nameof(FocusBorderBrush));
                    OnPropertyChanged(nameof(FocusBorderThickness));
                }
            }
        }

        public void Dispose()
        {
            if (_gamepadNavigationService != null)
            {
                _gamepadNavigationService.GamepadActiveStateChanged -= OnGamepadActiveStateChanged;
            }
            this.Loaded -= NavigableExpander_Loaded;
        }
    }
}
