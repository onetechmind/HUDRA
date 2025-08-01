using System;

namespace HUDRA.Services.Power
{
    public class PowerProfile
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public PowerProfileType Type { get; set; }
        public string Description { get; set; } = string.Empty;

        public string TypeIcon => Type switch
        {
            PowerProfileType.WindowsBuiltIn when Name.Contains("Power saver", StringComparison.OrdinalIgnoreCase) => "\uE83F",
            PowerProfileType.WindowsBuiltIn when Name.Contains("High performance", StringComparison.OrdinalIgnoreCase) => "\uE945",
            PowerProfileType.WindowsBuiltIn => "\uE713",
            PowerProfileType.ManufacturerCustom => "\uE90F",
            PowerProfileType.UserCreated => "\uE90F",
            _ => "\uE713"
        };

        public Microsoft.UI.Xaml.Visibility IsActiveVisibility => IsActive ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    }

    public enum PowerProfileType
    {
        WindowsBuiltIn,
        ManufacturerCustom,
        UserCreated,
        Unknown
    }
}