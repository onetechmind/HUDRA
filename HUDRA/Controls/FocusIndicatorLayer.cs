using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using HUDRA.Services;

namespace HUDRA.Controls
{
    /// <summary>
    /// Single shared gamepad focus ring, drawn on a hit-test-invisible canvas
    /// above the page content. Replaces the per-control FocusBorderBrush
    /// properties: any plain focus candidate gets the ring automatically
    /// (DarkViolet focused, DodgerBlue while value-editing). Legacy
    /// IGamepadNavigable controls keep self-rendering until migrated - the
    /// service excludes them from CurrentFocusVisualTarget.
    ///
    /// Tracks the target through scrolling and layout changes via
    /// EffectiveViewportChanged + SizeChanged on the focused element.
    /// </summary>
    public sealed partial class FocusIndicatorLayer : Canvas
    {
        private const double RingInflation = 3;
        private static readonly SolidColorBrush FocusBrush = new(Microsoft.UI.Colors.DarkViolet);
        private static readonly SolidColorBrush EditBrush = new(Microsoft.UI.Colors.DodgerBlue);

        private readonly Border _ring;
        private GamepadNavigationService? _service;
        private FrameworkElement? _target;

        public FocusIndicatorLayer()
        {
            IsHitTestVisible = false;

            _ring = new Border
            {
                BorderBrush = FocusBrush,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(6),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            Children.Add(_ring);
        }

        /// <summary>Wire the layer to the navigation service (call once at startup).</summary>
        public void Attach(GamepadNavigationService service)
        {
            _service = service;
            service.FocusVisualStateChanged += (s, e) => UpdateState();
        }

        private void UpdateState()
        {
            var newTarget = _service?.CurrentFocusVisualTarget;

            if (!ReferenceEquals(newTarget, _target))
            {
                if (_target != null)
                {
                    _target.EffectiveViewportChanged -= OnTargetViewportChanged;
                    _target.SizeChanged -= OnTargetSizeChanged;
                }

                _target = newTarget;

                if (_target != null)
                {
                    _target.EffectiveViewportChanged += OnTargetViewportChanged;
                    _target.SizeChanged += OnTargetSizeChanged;
                }
            }

            if (_target == null)
            {
                _ring.Visibility = Visibility.Collapsed;
                return;
            }

            _ring.BorderBrush = _service?.IsValueEditing == true ? EditBrush : FocusBrush;
            Reposition();
        }

        private void OnTargetViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args) => Reposition();

        private void OnTargetSizeChanged(object sender, SizeChangedEventArgs e) => Reposition();

        private void Reposition()
        {
            if (_target == null) return;

            try
            {
                if (!_target.IsLoaded || _target.ActualWidth <= 0 || _target.ActualHeight <= 0)
                {
                    _ring.Visibility = Visibility.Collapsed;
                    return;
                }

                var origin = _target.TransformToVisual(this).TransformPoint(new Point(0, 0));
                SetLeft(_ring, origin.X - RingInflation);
                SetTop(_ring, origin.Y - RingInflation);
                _ring.Width = _target.ActualWidth + 2 * RingInflation;
                _ring.Height = _target.ActualHeight + 2 * RingInflation;
                _ring.Visibility = Visibility.Visible;
            }
            catch
            {
                // Target detached from the tree - hide until the next focus change
                _ring.Visibility = Visibility.Collapsed;
            }
        }
    }
}
