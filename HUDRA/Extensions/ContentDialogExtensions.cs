using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using HUDRA.Services;

namespace HUDRA.Extensions
{
    /// <summary>
    /// Extension methods for ContentDialog to provide automatic gamepad support
    /// </summary>
    public static class ContentDialogExtensions
    {
        /// <summary>
        /// Shows a ContentDialog with automatic gamepad navigation support.
        /// This method automatically handles dialog state management for gamepad input,
        /// allowing A/B buttons to control the dialog without manual setup.
        /// </summary>
        /// <param name="dialog">The ContentDialog to show</param>
        /// <param name="gamepadService">The GamepadNavigationService instance</param>
        /// <returns>The ContentDialogResult indicating which button was pressed</returns>
        public static async Task<ContentDialogResult> ShowWithGamepadSupportAsync(
            this ContentDialog dialog,
            GamepadNavigationService gamepadService)
        {
            // Set up gamepad handling for this dialog
            gamepadService.SetDialogOpen(dialog);

            try
            {
                // Show the dialog
                return await dialog.ShowAsync();
            }
            finally
            {
                // Clean up gamepad state when dialog closes
                gamepadService.SetDialogClosed();
            }
        }
    }
}
