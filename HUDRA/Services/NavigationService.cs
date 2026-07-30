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

        /// <summary>
        /// Raised after the frame content has been replaced. Carries whether the
        /// navigation originated from the gamepad, so focus behaviour does not
        /// depend on a separate flag that can leak when a navigation is rejected.
        /// </summary>
        public event EventHandler<PageChangedEventArgs>? PageChanged;
        public bool IsNavigating => _isNavigating;
        public Type? CurrentPageType => _currentPageType;

        public NavigationService(Frame frame)
        {
            _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        }

        public void NavigateToMain(bool fromGamepad = false)
        {
            Navigate(typeof(MainPage), fromGamepad);
        }

        public void NavigateToSettings(bool fromGamepad = false)
        {
            Navigate(typeof(SettingsPage), fromGamepad);
        }

        public void NavigateToFanCurve(bool fromGamepad = false)
        {
            Navigate(typeof(FanCurvePage), fromGamepad);
        }

        public void NavigateToScaling(bool fromGamepad = false)
        {
            Navigate(typeof(ScalingPage), fromGamepad);
        }

        public void NavigateToLibrary(bool fromGamepad = false)
        {
            Navigate(typeof(LibraryPage), fromGamepad);
        }

        public void NavigateToGameSettings(bool fromGamepad = false)
        {
            Navigate(typeof(GameSettingsPage), fromGamepad);
        }

        public void Navigate(Type pageType, bool fromGamepad = false)
        {
            if (pageType == null) throw new ArgumentNullException(nameof(pageType));
            if (_isNavigating) return; // Prevent concurrent navigation

            try
            {
                _isNavigating = true;
                
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

                                        
                    // Notify after content is set
                    PageChanged?.Invoke(this, new PageChangedEventArgs(pageType, fromGamepad));
                }
                else
                {
                                    }
            }
            catch (Exception ex)
            {
                                            }
            finally
            {
                // Clear navigation flag after a small delay to ensure page transition completes
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                timer.Tick += (s, e) =>
                {
                    timer.Stop();
                    _isNavigating = false;
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

    /// <summary>Details of a completed page navigation.</summary>
    public sealed class PageChangedEventArgs : EventArgs
    {
        public PageChangedEventArgs(Type pageType, bool fromGamepad)
        {
            PageType = pageType;
            FromGamepad = fromGamepad;
        }

        public Type PageType { get; }

        /// <summary>True when the navigation was initiated by gamepad input.</summary>
        public bool FromGamepad { get; }
    }
}
