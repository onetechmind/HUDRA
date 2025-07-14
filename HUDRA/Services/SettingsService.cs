using System;
using System.IO;
using Windows.Storage;

namespace HUDRA.Services
{
    public static class SettingsService
    {
        private const string TdpCorrectionKey = "TdpCorrectionEnabled";
        private static readonly string FallbackPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HUDRA",
            "settings.txt");

        public static bool GetTdpCorrectionEnabled()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue(TdpCorrectionKey, out var value) && value is bool b)
                {
                    return b;
                }
                return true;
            }
            catch (Exception)
            {
                return LoadFallback();
            }
        }

        public static void SetTdpCorrectionEnabled(bool enabled)
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                settings.Values[TdpCorrectionKey] = enabled;
            }
            catch (Exception)
            {
                SaveFallback(enabled);
            }
        }

        private static bool LoadFallback()
        {
            try
            {
                if (File.Exists(FallbackPath))
                {
                    var text = File.ReadAllText(FallbackPath);
                    if (bool.TryParse(text, out var value))
                        return value;
                }
            }
            catch
            {
            }
            return true;
        }

        private static void SaveFallback(bool enabled)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FallbackPath)!);
                File.WriteAllText(FallbackPath, enabled.ToString());
            }
            catch
            {
            }
        }
    }
}
