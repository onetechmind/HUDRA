using Microsoft.UI.Xaml.Controls;
using System;

namespace HUDRA.Services
{
    public class NavigationService
    {
        private readonly Frame _frame;

        public NavigationService(Frame frame)
        {
            _frame = frame ?? throw new ArgumentNullException(nameof(frame));
        }

        public void Navigate(Type pageType)
        {
            if (pageType == null) throw new ArgumentNullException(nameof(pageType));
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

