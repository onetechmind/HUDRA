using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;

namespace HUDRA.Services
{
    public interface INavigationService
    {
        bool CanGoBack { get; }
        void GoBack();
        bool Navigate<T>(object? parameter = null);
        bool Navigate(Type sourcePageType, object? parameter = null);
        event NavigatedEventHandler? Navigated;
    }

    public class NavigationService : INavigationService
    {
        private readonly Frame _frame;
        private readonly Stack<Type> _history = new();
        public event NavigatedEventHandler? Navigated;
        public NavigationService(Frame frame)
        {
            _frame = frame ?? throw new ArgumentNullException(nameof(frame));
            _frame.Navigated += OnFrameNavigated;
        }

        public bool CanGoBack => _frame.CanGoBack;

        public void GoBack()
        {
            if (CanGoBack)
            {
                _frame.GoBack();
            }
        }

        public bool Navigate<T>(object? parameter = null)
        {
            return Navigate(typeof(T), parameter);
        }


            _frame.Navigate(pageType);

        public bool Navigate(Type sourcePageType, object? parameter = null)
        {
            if (sourcePageType == null)
                throw new ArgumentNullException(nameof(sourcePageType));
            try
            {
                return _frame.Navigate(sourcePageType, parameter);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Navigation to {sourcePageType.Name} failed: {ex.Message}");
                return false;
            }
        }

        private void OnFrameNavigated(object sender, NavigationEventArgs e)
        {

            if (_frame.CanGoBack)
            {
                _frame.GoBack();

            if (e.NavigationMode == NavigationMode.New)
            {
                _history.Push(e.SourcePageType);
            }
            Navigated?.Invoke(sender, e);
        }
    }
}
