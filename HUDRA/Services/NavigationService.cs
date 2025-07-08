// Update your NavigationService.cs to use manual content setting:
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;

namespace HUDRA.Services
{
    public class NavigationService
    {
        private readonly Frame _frame;
        private readonly Stack<UserControl> _navigationStack = new();

        public NavigationService(Frame frame)
        {
            _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        }

        public void Navigate(Type pageType)
        {
            if (pageType == null) throw new ArgumentNullException(nameof(pageType));

            try
            {
                // Save current content to navigation stack
                if (_frame.Content is UserControl currentPage)
                {
                    _navigationStack.Push(currentPage);
                }

                // Create new page instance manually instead of using Frame.Navigate()
                var newPage = Activator.CreateInstance(pageType) as UserControl;
                if (newPage != null)
                {
                    _frame.Content = newPage;
                    System.Diagnostics.Debug.WriteLine($"Manually navigated to {pageType.Name}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Manual navigation failed: {ex.Message}");
            }
        }

        public void GoBack()
        {
            if (_navigationStack.Count > 0)
            {
                var previousPage = _navigationStack.Pop();
                _frame.Content = previousPage;
                System.Diagnostics.Debug.WriteLine("Navigated back manually");
            }
        }
    }
}