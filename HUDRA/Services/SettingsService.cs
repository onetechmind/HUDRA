using Windows.Storage;

namespace HUDRA.Services
{
    public static class SettingsService
    {
        private const string TdpCorrectionKey = "TdpCorrectionEnabled";

        public static bool GetTdpCorrectionEnabled()
        {
            var settings = ApplicationData.Current.LocalSettings;
            if (settings.Values.TryGetValue(TdpCorrectionKey, out var value) && value is bool b)
            {
                return b;
            }
            return true;
        }

        public static void SetTdpCorrectionEnabled(bool enabled)
        {
            var settings = ApplicationData.Current.LocalSettings;
            settings.Values[TdpCorrectionKey] = enabled;
        }
    }
}
