using LibreHardwareMonitor.Hardware;
using Microsoft.UI.Dispatching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Threading;

namespace HUDRA.Services
{
    // Keep your existing TemperatureData class
    public class TemperatureData
    {
        public double CpuTemperature { get; set; } = 0;
        public double GpuTemperature { get; set; } = 0;
        public double MaxTemperature => Math.Max(CpuTemperature, GpuTemperature);
        public string Source { get; set; } = "Unknown";
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }

    // Keep your existing TemperatureChangedEventArgs class
    public class TemperatureChangedEventArgs : EventArgs
    {
        public TemperatureData TemperatureData { get; }

        public TemperatureChangedEventArgs(TemperatureData data)
        {
            TemperatureData = data;
        }
    }

    // Enhanced TemperatureMonitorService with LibreHardwareMonitor
    public class TemperatureMonitorService : IDisposable
    {
        private readonly DispatcherQueue _dispatcher;
        private readonly Timer _monitoringTimer;
        private bool _disposed = false;
        private TemperatureData _currentTemperatureData = new();

        // ADD: LibreHardwareMonitor integration
        private Computer? _computer;
        private bool _useLibreHardwareMonitor = false;

        public event EventHandler<TemperatureChangedEventArgs>? TemperatureChanged;
        public TemperatureData CurrentTemperature => _currentTemperatureData;

        public TemperatureMonitorService(DispatcherQueue dispatcher)
        {
            _dispatcher = dispatcher;

            // Initialize LibreHardwareMonitor
            InitializeLibreHardwareMonitor();

            // Start monitoring every 2 seconds
            _monitoringTimer = new Timer(MonitorTemperatures, null,
                TimeSpan.FromSeconds(1), // Start immediately
                TimeSpan.FromSeconds(2)); // Then every 2 seconds

            System.Diagnostics.Debug.WriteLine($"Temperature monitoring service started (LibreHW: {_useLibreHardwareMonitor})");
        }

        private void InitializeLibreHardwareMonitor()
        {
            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMemoryEnabled = false,    // Don't need these for temperature
                    IsMotherboardEnabled = false,
                    IsControllerEnabled = false,
                    IsNetworkEnabled = false,
                    IsStorageEnabled = false
                };

                _computer.Open();
                _useLibreHardwareMonitor = true;

                System.Diagnostics.Debug.WriteLine("✅ LibreHardwareMonitor initialized successfully");

                // Debug: List available sensors
                LogAvailableSensors();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️ LibreHardwareMonitor init failed: {ex.Message}");
                _useLibreHardwareMonitor = false;
                _computer?.Close();
                _computer = null;
            }
        }

        private void LogAvailableSensors()
        {
            if (_computer == null) return;

            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    hardware.Update();
                    System.Diagnostics.Debug.WriteLine($"Hardware: {hardware.Name} ({hardware.HardwareType})");

                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Temperature)
                        {
                            System.Diagnostics.Debug.WriteLine($"  Temp Sensor: {sensor.Name} = {sensor.Value:F1}°C");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error logging sensors: {ex.Message}");
            }
        }

        private void MonitorTemperatures(object? state)
        {
            if (_disposed) return;

            try
            {
                TemperatureData newData;

                if (_useLibreHardwareMonitor)
                {
                    newData = ReadTemperaturesFromLibreHardware();
                }
                else
                {
                    newData = ReadTemperaturesFromWMI(); // Fallback to your existing method
                }

                // Only update if temperature changed significantly (> 1°C)
                if (Math.Abs(newData.MaxTemperature - _currentTemperatureData.MaxTemperature) > 1.0)
                {
                    _currentTemperatureData = newData;

                    _dispatcher.TryEnqueue(() =>
                    {
                        TemperatureChanged?.Invoke(this, new TemperatureChangedEventArgs(_currentTemperatureData));
                    });

                    System.Diagnostics.Debug.WriteLine($"Temperature updated: CPU={newData.CpuTemperature:F1}°C, GPU={newData.GpuTemperature:F1}°C, Max={newData.MaxTemperature:F1}°C");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Temperature monitoring error: {ex.Message}");
            }
        }

        private TemperatureData ReadTemperaturesFromLibreHardware()
        {
            var result = new TemperatureData { Source = "LibreHardwareMonitor" };

            try
            {
                if (_computer == null) return ReadTemperaturesFromWMI();

                foreach (var hardware in _computer.Hardware)
                {
                    hardware.Update();

                    // Get CPU temperatures
                    if (hardware.HardwareType == HardwareType.Cpu)
                    {
                        var cpuTemps = hardware.Sensors
                            .Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue)
                            .Select(s => s.Value.Value)
                            .Where(temp => temp > 20 && temp < 100)
                            .ToList();

                        if (cpuTemps.Any())
                        {
                            // Use the highest CPU temperature (usually "CPU Package" or core max)
                            result.CpuTemperature = cpuTemps.Max();
                        }
                    }

                    // Get GPU temperatures
                    if (hardware.HardwareType == HardwareType.GpuNvidia ||
                        hardware.HardwareType == HardwareType.GpuAmd ||
                        hardware.HardwareType == HardwareType.GpuIntel)
                    {
                        var gpuTemps = hardware.Sensors
                            .Where(s => s.SensorType == SensorType.Temperature && s.Value.HasValue)
                            .Select(s => s.Value.Value)
                            .Where(temp => temp > 20 && temp < 100)
                            .ToList();

                        if (gpuTemps.Any())
                        {
                            // Use the highest GPU temperature
                            result.GpuTemperature = Math.Max(result.GpuTemperature, gpuTemps.Max());
                        }
                    }
                }

                // Fallback to WMI if no temperatures found
                if (result.CpuTemperature == 0)
                {
                    var wmiData = ReadTemperaturesFromWMI();
                    result.CpuTemperature = wmiData.CpuTemperature;
                    result.Source = "LibreHW + WMI Fallback";
                }

                result.LastUpdated = DateTime.Now;
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LibreHardwareMonitor read error: {ex.Message}");
                return ReadTemperaturesFromWMI(); // Fallback
            }
        }

        // Keep your existing WMI methods as fallback
        private TemperatureData ReadTemperaturesFromWMI()
        {
            var result = new TemperatureData();

            try
            {
                // Method 1: Try MSAcpi_ThermalZoneTemperature (most common)
                var cpuTemp = ReadThermalZoneTemperature();
                if (cpuTemp > 0)
                {
                    result.CpuTemperature = cpuTemp;
                    result.Source = "Thermal Zone";
                }

                // Method 2: Try Win32_TemperatureProbe 
                if (result.CpuTemperature == 0)
                {
                    var probeTemp = ReadTemperatureProbe();
                    if (probeTemp > 0)
                    {
                        result.CpuTemperature = probeTemp;
                        result.Source = "Temperature Probe";
                    }
                }

                // Method 3: Try OpenHardwareMonitor/LibreHardwareMonitor WMI (if installed)
                var ohwmTemps = ReadOpenHardwareMonitorWMI();
                if (ohwmTemps.Count > 0)
                {
                    // Use the highest CPU temp if multiple cores
                    var cpuTemps = ohwmTemps.Where(t => t.Key.Contains("CPU", StringComparison.OrdinalIgnoreCase));
                    if (cpuTemps.Any())
                    {
                        result.CpuTemperature = cpuTemps.Max(t => t.Value);
                    }

                    // Use GPU temp if available
                    var gpuTemps = ohwmTemps.Where(t => t.Key.Contains("GPU", StringComparison.OrdinalIgnoreCase));
                    if (gpuTemps.Any())
                    {
                        result.GpuTemperature = gpuTemps.Max(t => t.Value);
                    }

                    if (cpuTemps.Any() || gpuTemps.Any())
                    {
                        result.Source = "OpenHardwareMonitor";
                    }
                }

                // Fallback: Use the higher of CPU or estimated temperature
                if (result.CpuTemperature == 0)
                {
                    result.CpuTemperature = EstimateTemperatureFromCPU();
                    result.Source = "Estimated";
                }

                // Clamp values to reasonable ranges
                result.CpuTemperature = Math.Clamp(result.CpuTemperature, 20, 100);
                result.GpuTemperature = Math.Clamp(result.GpuTemperature, 20, 100);
                result.LastUpdated = DateTime.Now;

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading WMI temperatures: {ex.Message}");

                // Return safe fallback values
                return new TemperatureData
                {
                    CpuTemperature = EstimateTemperatureFromCPU(),
                    GpuTemperature = 0,
                    Source = "Fallback",
                    LastUpdated = DateTime.Now
                };
            }
        }

        private double ReadThermalZoneTemperature()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("root\\WMI",
                    "SELECT * FROM MSAcpi_ThermalZoneTemperature");

                var temperatures = new List<double>();

                foreach (var obj in searcher.Get())
                {
                    var tempValue = obj["CurrentTemperature"];
                    if (tempValue != null)
                    {
                        // Convert from tenths of Kelvin to Celsius
                        var kelvinTenths = Convert.ToDouble(tempValue);
                        var celsius = (kelvinTenths / 10.0) - 273.15;

                        if (celsius > 20 && celsius < 100) // Reasonable range
                        {
                            temperatures.Add(celsius);
                        }
                    }
                }

                return temperatures.Count > 0 ? temperatures.Max() : 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Thermal zone reading failed: {ex.Message}");
                return 0;
            }
        }

        private double ReadTemperatureProbe()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("root\\CIMV2",
                    "SELECT * FROM Win32_TemperatureProbe");

                foreach (var obj in searcher.Get())
                {
                    var tempValue = obj["CurrentReading"];
                    if (tempValue != null)
                    {
                        var kelvinTenths = Convert.ToDouble(tempValue);
                        var celsius = (kelvinTenths / 10.0) - 273.15;

                        if (celsius > 20 && celsius < 100)
                        {
                            return celsius;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Temperature probe reading failed: {ex.Message}");
            }

            return 0;
        }

        private Dictionary<string, double> ReadOpenHardwareMonitorWMI()
        {
            var results = new Dictionary<string, double>();

            try
            {
                // Try LibreHardwareMonitor first (newer)
                using var searcher = new ManagementObjectSearcher("root\\LibreHardwareMonitor",
                    "SELECT * FROM Sensor WHERE SensorType = 'Temperature'");

                foreach (var obj in searcher.Get())
                {
                    var name = obj["Name"]?.ToString();
                    var value = obj["Value"];

                    if (name != null && value != null)
                    {
                        if (double.TryParse(value.ToString(), out double temp) && temp > 20 && temp < 100)
                        {
                            results[name] = temp;
                        }
                    }
                }
            }
            catch
            {
                // LibreHardwareMonitor not available, try OpenHardwareMonitor
                try
                {
                    using var searcher = new ManagementObjectSearcher("root\\OpenHardwareMonitor",
                        "SELECT * FROM Sensor WHERE SensorType = 'Temperature'");

                    foreach (var obj in searcher.Get())
                    {
                        var name = obj["Name"]?.ToString();
                        var value = obj["Value"];

                        if (name != null && value != null)
                        {
                            if (double.TryParse(value.ToString(), out double temp) && temp > 20 && temp < 100)
                            {
                                results[name] = temp;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"OpenHardwareMonitor WMI not available: {ex.Message}");
                }
            }

            return results;
        }

        private double EstimateTemperatureFromCPU()
        {
            try
            {
                // Simple estimation: base temp + usage factor
                var baseTemp = 35.0; // Idle temperature estimate
                var maxTempRange = 40.0; // Max additional temp from usage

                // Get CPU usage (simplified)
                using var searcher = new ManagementObjectSearcher("root\\CIMV2",
                    "SELECT LoadPercentage FROM Win32_Processor");

                foreach (var obj in searcher.Get())
                {
                    var loadValue = obj["LoadPercentage"];
                    if (loadValue != null && double.TryParse(loadValue.ToString(), out double cpuUsage))
                    {
                        // Estimate temperature based on CPU usage
                        var estimatedTemp = baseTemp + (cpuUsage / 100.0 * maxTempRange);
                        return Math.Clamp(estimatedTemp, 25, 85);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CPU estimation failed: {ex.Message}");
            }

            return 45.0; // Safe default
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _monitoringTimer?.Dispose();

                // Dispose LibreHardwareMonitor
                try
                {
                    _computer?.Close();
                    _computer = null;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error disposing LibreHardwareMonitor: {ex.Message}");
                }

                System.Diagnostics.Debug.WriteLine("Temperature monitoring service disposed");
            }
        }
    }
}