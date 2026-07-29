using System;
using Microsoft.UI.Xaml;

namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Wraps a page that drives its own gamepad navigation, binding the scope's
    /// lifetime to that page actually being the displayed one.
    ///
    /// Page initialization is asynchronous, so a scope can be installed by a
    /// continuation that resumes after the user has already navigated elsewhere.
    /// Previously that left a detached page consuming every directional press on
    /// every subsequent page, healed only by revisiting the page it belonged to.
    /// Now the router simply reaps it on the next input.
    /// </summary>
    public sealed class PageOwnedScope : IInputScope
    {
        private readonly IInputScope _inner;
        private readonly FrameworkElement _page;
        private readonly Func<object?> _currentContent;

        public PageOwnedScope(IInputScope inner, FrameworkElement page, Func<object?> currentContent)
        {
            _inner = inner;
            _page = page;
            _currentContent = currentContent;
        }

        public string Name => $"{_inner.Name}@{_page.GetType().Name}";

        public int Layer => ScopeLayer.PageCustom;

        public bool OwnsFocusVisuals => _inner.OwnsFocusVisuals;

        /// <summary>Valid only while the owning page is still the displayed content.</summary>
        public bool IsStillValid
        {
            get
            {
                try { return ReferenceEquals(_currentContent(), _page); }
                catch { return true; }
            }
        }

        public bool HandleEvent(in GamepadEvent e) => _inner.HandleEvent(in e);

        public void OnStickFrame(in GamepadStickFrame frame) => _inner.OnStickFrame(in frame);

        public void OnPushed(InputRouter router) => _inner.OnPushed(router);

        public void OnPopped() => _inner.OnPopped();
    }
}
