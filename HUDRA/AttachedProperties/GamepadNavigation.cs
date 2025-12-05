using HUDRA.Interfaces;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;
using System.Linq;

namespace HUDRA.AttachedProperties
{
    public static class GamepadNavigation
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(GamepadNavigation),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static readonly DependencyProperty NavigationGroupProperty =
            DependencyProperty.RegisterAttached(
                "NavigationGroup",
                typeof(string),
                typeof(GamepadNavigation),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty NavigationOrderProperty =
            DependencyProperty.RegisterAttached(
                "NavigationOrder",
                typeof(int),
                typeof(GamepadNavigation),
                new PropertyMetadata(0));

        public static readonly DependencyProperty IsCurrentFocusProperty =
            DependencyProperty.RegisterAttached(
                "IsCurrentFocus",
                typeof(bool),
                typeof(GamepadNavigation),
                new PropertyMetadata(false, OnIsCurrentFocusChanged));

        public static readonly DependencyProperty CanNavigateProperty =
            DependencyProperty.RegisterAttached(
                "CanNavigate",
                typeof(bool),
                typeof(GamepadNavigation),
                new PropertyMetadata(true));

        private static readonly DependencyProperty NavigationHelperProperty =
            DependencyProperty.RegisterAttached(
                "NavigationHelper",
                typeof(GamepadNavigationHelper),
                typeof(GamepadNavigation),
                new PropertyMetadata(null));

        public static bool GetIsEnabled(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsEnabledProperty);
        }

        public static void SetIsEnabled(DependencyObject obj, bool value)
        {
            obj.SetValue(IsEnabledProperty, value);
        }

        public static string GetNavigationGroup(DependencyObject obj)
        {
            return (string)obj.GetValue(NavigationGroupProperty);
        }

        public static void SetNavigationGroup(DependencyObject obj, string value)
        {
            obj.SetValue(NavigationGroupProperty, value);
        }

        public static int GetNavigationOrder(DependencyObject obj)
        {
            return (int)obj.GetValue(NavigationOrderProperty);
        }

        public static void SetNavigationOrder(DependencyObject obj, int value)
        {
            obj.SetValue(NavigationOrderProperty, value);
        }

        public static bool GetIsCurrentFocus(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsCurrentFocusProperty);
        }

        public static void SetIsCurrentFocus(DependencyObject obj, bool value)
        {
            obj.SetValue(IsCurrentFocusProperty, value);
        }

        public static bool GetCanNavigate(DependencyObject obj)
        {
            return (bool)obj.GetValue(CanNavigateProperty);
        }

        public static void SetCanNavigate(DependencyObject obj, bool value)
        {
            obj.SetValue(CanNavigateProperty, value);
        }

        private static GamepadNavigationHelper GetNavigationHelper(DependencyObject obj)
        {
            return (GamepadNavigationHelper)obj.GetValue(NavigationHelperProperty);
        }

        private static void SetNavigationHelper(DependencyObject obj, GamepadNavigationHelper value)
        {
            obj.SetValue(NavigationHelperProperty, value);
        }

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element)
            {
                if ((bool)e.NewValue)
                {
                    var helper = new GamepadNavigationHelper(element);
                    SetNavigationHelper(element, helper);
                    System.Diagnostics.Debug.WriteLine($"🎮 Enabled gamepad navigation for {element.GetType().Name}");
                }
                else
                {
                    var helper = GetNavigationHelper(element);
                    helper?.Dispose();
                    SetNavigationHelper(element, null);
                    System.Diagnostics.Debug.WriteLine($"🎮 Disabled gamepad navigation for {element.GetType().Name}");
                }
            }
        }

        private static void OnIsCurrentFocusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element)
            {
                bool hasFocus = (bool)e.NewValue;
                var helper = GetNavigationHelper(element);
                if (helper != null)
                {
                    helper.OnFocusChanged(hasFocus);
                    System.Diagnostics.Debug.WriteLine($"🎮 Updated navigation helper for {element.GetType().Name}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"🎮 No navigation helper found for {element.GetType().Name}");
                }
            }
        }

        public static IEnumerable<FrameworkElement> GetNavigableElements(FrameworkElement root, string? group = null)
        {
            var elements = new List<FrameworkElement>();
            
            // First check if the root element itself is navigable
            if (GetIsEnabled(root) && GetCanNavigate(root))
            {
                var rootGroup = GetNavigationGroup(root);
                if (group == null || rootGroup == group)
                {
                    elements.Add(root);
                }
            }
            
            // Then collect child elements
            CollectNavigableElements(root, elements, group);
            
            return elements.OrderBy(e => GetNavigationOrder(e));
        }

        private static void CollectNavigableElements(DependencyObject parent, List<FrameworkElement> elements, string? targetGroup)
        {
            for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);

                if (child is FrameworkElement element)
                {
                    // Only process and recurse into visible and enabled elements
                    // IsEnabled is on Control, not FrameworkElement, so check if it's a Control first
                    bool isEnabled = element is not Control control || control.IsEnabled;
                    if (element.Visibility == Visibility.Visible && isEnabled)
                    {
                        if (GetIsEnabled(element) && GetCanNavigate(element))
                        {
                            var group = GetNavigationGroup(element);
                            if (targetGroup == null || group == targetGroup)
                            {
                                elements.Add(element);
                            }
                        }

                        // Only recurse into children if parent is visible and enabled
                        CollectNavigableElements(child, elements, targetGroup);
                    }
                }
            }
        }

        public static bool IsNavigableControl(FrameworkElement element)
        {
            return element is Button ||
                   element is Slider ||
                   element is ComboBox ||
                   element is CheckBox ||
                   element is RadioButton ||
                   element is Microsoft.UI.Xaml.Controls.Primitives.ToggleButton ||
                   element is RatingControl ||
                   element is IGamepadNavigable;
        }
    }

    public class GamepadNavigationHelper : System.IDisposable
    {
        private readonly FrameworkElement _element;
        private IGamepadNavigable? _navigableControl;

        public GamepadNavigationHelper(FrameworkElement element)
        {
            _element = element;
            _navigableControl = element as IGamepadNavigable;
            
            // Subscribe to events if needed
            _element.Loaded += OnElementLoaded;
        }

        private void OnElementLoaded(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"🎮 GamepadNavigationHelper loaded for {_element.GetType().Name}");
        }

        public void OnFocusChanged(bool hasFocus)
        {
            if (_navigableControl != null)
            {
                if (hasFocus)
                {
                    _navigableControl.OnGamepadFocusReceived();
                }
                else
                {
                    _navigableControl.OnGamepadFocusLost();
                }
            }
        }

        public void Dispose()
        {
            _element.Loaded -= OnElementLoaded;
        }
    }
}