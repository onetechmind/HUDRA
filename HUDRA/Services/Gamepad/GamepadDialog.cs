using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;

namespace HUDRA.Services.GamepadInput
{
    /// <summary>
    /// Single entry point for showing ContentDialogs. Guarantees gamepad
    /// support (DialogScope push/pop around ShowAsync) and serializes dialogs:
    /// WinUI throws if two ContentDialogs are shown on the same XamlRoot at
    /// once, so concurrent requests queue instead of crashing.
    /// </summary>
    public static class GamepadDialog
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);

        public static async Task<ContentDialogResult> ShowAsync(
            ContentDialog dialog,
            Services.GamepadNavigationService? gamepadService)
        {
            await Gate.WaitAsync();
            try
            {
                gamepadService?.SetDialogOpen(dialog);
                try
                {
                    return await dialog.ShowAsync();
                }
                finally
                {
                    gamepadService?.SetDialogClosed();
                }
            }
            finally
            {
                Gate.Release();
            }
        }
    }
}
