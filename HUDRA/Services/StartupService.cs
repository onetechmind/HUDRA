using System;
using System.IO;
using System.Runtime.InteropServices;

namespace HUDRA.Services
{
    public static class StartupService
    {
        private static readonly string StartupFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        private static readonly string ShortcutPath = Path.Combine(StartupFolderPath, "HUDRA.lnk");

        public static void EnableLaunchAtStartup()
        {
            try
            {
                if (!File.Exists(ShortcutPath))
                {
                    Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                    if (shellType != null)
                    {
                        dynamic shell = Activator.CreateInstance(shellType)!;
                        dynamic shortcut = shell.CreateShortcut(ShortcutPath);
                        shortcut.TargetPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                        shortcut.WorkingDirectory = AppContext.BaseDirectory;
                        shortcut.Save();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create startup shortcut: {ex.Message}");
            }
        }

        public static void DisableLaunchAtStartup()
        {
            try
            {
                if (File.Exists(ShortcutPath))
                {
                    File.Delete(ShortcutPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to remove startup shortcut: {ex.Message}");
            }
        }

        public static bool IsLaunchAtStartupEnabled()
        {
            return File.Exists(ShortcutPath);
        }
    }
}
