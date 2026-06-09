using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using HUDRA.Services;
using HUDRA.Services.GamepadInput;

namespace HUDRA.Extensions
{
    /// <summary>
    /// Extension methods for ContentDialog to provide automatic gamepad support
    /// </summary>
    public static class ContentDialogExtensions
    {
        /// <summary>
        /// Shows a ContentDialog with gamepad navigation support: d-pad moves
        /// between the dialog buttons, A invokes the focused button, B cancels.
        /// Dialogs are serialized so concurrent calls cannot crash ShowAsync.
        /// </summary>
        public static Task<ContentDialogResult> ShowWithGamepadSupportAsync(
            this ContentDialog dialog,
            GamepadNavigationService? gamepadService)
        {
            return GamepadDialog.ShowAsync(dialog, gamepadService);
        }
    }
}
