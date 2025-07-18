using HUDRA.Pages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;

namespace HUDRA.Services
{
    public class NavigationService : IDisposable
    {
        private readonly Frame _frame;
        private readonly Stack<Type> _navigationStack = new();
        private Type? _currentPageType;
        private bool _isNavigating = false;

        public event EventHandler<Type>? PageChanged;
        public bool IsNavigating => _isNavigating;
        public Type? CurrentPageType => _currentPageType;

        public NavigationService(Frame frame)
        {
            _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        }

        public void NavigateToMain()
        {
            Navigate(typeof(MainPage));
        }

        public void NavigateToSettings()
        {
            Navigate(typeof(SettingsPage));
        }

        public void Navigate(Type pageType)
        {
            if (pageType == null) throw new ArgumentNullException(nameof(pageType));
            if (_isNavigating) return; // Prevent concurrent navigation

            try
            {
                _isNavigating = true;
                System.Diagnostics.Debug.WriteLine($"Navigation starting to {pageType.Name}");

                // Save current page type to stack if different
                if (_currentPageType != null && _currentPageType != pageType)
                {
                    _navigationStack.Push(_currentPageType);
                }

                // Create new page instance
                var newPage = Activator.CreateInstance(pageType) as FrameworkElement;
                if (newPage != null)
                {
                    _frame.Content = newPage;
                    _currentPageType = pageType;

                    System.Diagnostics.Debug.WriteLine($"Navigation completed to {pageType.Name}");

                    // Notify after content is set
                    PageChanged?.Invoke(this, pageType);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Navigation failed: {ex.Message}");
            }
            finally
            {
                // Clear navigation flag after a small delay to ensure page transition completes
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    _isNavigating = false;
                    System.Diagnostics.Debug.WriteLine($"Navigation flag cleared");
                };
                timer.Start();
            }
        }

        public void GoBack()
        {
            if (_navigationStack.Count > 0 && !_isNavigating)
            {
                var previousPageType = _navigationStack.Pop();
                Navigate(previousPageType);
            }
        }

        public void Dispose()
        {
            PageChanged = null;
            _navigationStack.Clear();
        }
    }
}