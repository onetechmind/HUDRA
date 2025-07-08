using Microsoft.UI.Xaml.Controls;
using System;

namespace HUDRA.Services
{
    public class NavigationService
    {
        private readonly Frame _frame;

        public NavigationService(Frame frame)
        {
            _frame = frame;
        }

        public void Navigate(Type pageType)
        {
            _frame.Navigate(pageType);
        }

        public void GoBack()
        {
            if (_frame.CanGoBack)
            {
                _frame.GoBack();
            }
        }
    }
}

