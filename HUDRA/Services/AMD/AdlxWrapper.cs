using System;
using System.IO;
using System.Runtime.InteropServices;

namespace HUDRA.Services.AMD
{
    /// <summary>
    /// P/Invoke wrapper for ADLX SDK functions.
    /// Based on: https://github.com/JamesCJ60/ADLX-SDK-Wrapper
    /// </summary>
    public static class AdlxWrapper
    {
        // DLL Paths - assuming DLLs are in "External Resources/AMD/ADLX/" folder
        private const string ADLX_3D_SETTINGS_DLL = @"External Resources\AMD\ADLX\ADLX_3DSettings.dll";

        // Alternative: Try loading from application directory
        private static readonly string DLL_PATH = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "External Resources", "AMD", "ADLX", "ADLX_3DSettings.dll"
        );

        #region RSR (Radeon Super Resolution) Functions

        /// <summary>
        /// Check if the GPU supports Radeon Super Resolution
        /// </summary>
        /// <returns>True if RSR is supported, false otherwise</returns>
        [DllImport(ADLX_3D_SETTINGS_DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool HasRSRSupport();

        /// <summary>
        /// Get the current state of Radeon Super Resolution
        /// </summary>
        /// <returns>True if RSR is enabled, false if disabled</returns>
        [DllImport(ADLX_3D_SETTINGS_DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool GetRSRState();

        /// <summary>
        /// Enable or disable Radeon Super Resolution
        /// </summary>
        /// <param name="isEnabled">True to enable, false to disable</param>
        /// <returns>True if operation succeeded, false otherwise</returns>
        [DllImport(ADLX_3D_SETTINGS_DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool SetRSR(bool isEnabled);

        /// <summary>
        /// Get the current RSR sharpness level
        /// </summary>
        /// <returns>Sharpness value (0-100), or -1 on failure</returns>
        [DllImport(ADLX_3D_SETTINGS_DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetRSRSharpness();

        /// <summary>
        /// Set the RSR sharpness level
        /// </summary>
        /// <param name="sharpness">Sharpness value (0-100)</param>
        /// <returns>True if operation succeeded, false otherwise</returns>
        [DllImport(ADLX_3D_SETTINGS_DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool SetRSRSharpness(int sharpness);

        #endregion

        #region AFMF (AMD Fluid Motion Frames) Functions

        /// <summary>
        /// Check if the GPU supports AMD Fluid Motion Frames
        /// </summary>
        /// <returns>True if AFMF is supported, false otherwise</returns>
        [DllImport(ADLX_3D_SETTINGS_DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool HasAFMFSupport();

        /// <summary>
        /// Get the current state of AMD Fluid Motion Frames
        /// </summary>
        /// <returns>True if AFMF is enabled, false if disabled</returns>
        [DllImport(ADLX_3D_SETTINGS_DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool GetAFMFState();

        /// <summary>
        /// Enable or disable AMD Fluid Motion Frames
        /// </summary>
        /// <param name="isEnabled">True to enable, false to disable</param>
        /// <returns>True if operation succeeded, false otherwise</returns>
        [DllImport(ADLX_3D_SETTINGS_DLL, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool SetAFMFState(bool isEnabled);

        #endregion

        #region Helper Methods

        /// <summary>
        /// Check if ADLX DLL is available
        /// </summary>
        public static bool IsAdlxDllAvailable()
        {
            try
            {
                // Check if DLL exists in expected location
                if (File.Exists(DLL_PATH))
                {
                    System.Diagnostics.Debug.WriteLine($"Found ADLX DLL at: {DLL_PATH}");
                    return true;
                }

                // Check alternative path (relative to current directory)
                var alternativePath = Path.Combine(Directory.GetCurrentDirectory(), ADLX_3D_SETTINGS_DLL);
                if (File.Exists(alternativePath))
                {
                    System.Diagnostics.Debug.WriteLine($"Found ADLX DLL at: {alternativePath}");
                    return true;
                }

                System.Diagnostics.Debug.WriteLine($"ADLX DLL not found at: {DLL_PATH}");
                System.Diagnostics.Debug.WriteLine($"Also checked: {alternativePath}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking for ADLX DLL: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Safe wrapper for HasRSRSupport that handles DLL load failures
        /// </summary>
        public static bool TryHasRSRSupport(out bool supported)
        {
            supported = false;
            try
            {
                if (!IsAdlxDllAvailable())
                {
                    return false;
                }

                supported = HasRSRSupport();
                return true;
            }
            catch (DllNotFoundException ex)
            {
                System.Diagnostics.Debug.WriteLine($"ADLX DLL not found: {ex.Message}");
                return false;
            }
            catch (BadImageFormatException ex)
            {
                System.Diagnostics.Debug.WriteLine($"ADLX DLL architecture mismatch (x64 required): {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calling HasRSRSupport: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Safe wrapper for GetRSRState that handles DLL load failures
        /// </summary>
        public static bool TryGetRSRState(out bool enabled)
        {
            enabled = false;
            try
            {
                if (!IsAdlxDllAvailable())
                {
                    return false;
                }

                enabled = GetRSRState();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calling GetRSRState: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Safe wrapper for SetRSR that handles DLL load failures
        /// </summary>
        public static bool TrySetRSR(bool isEnabled, out bool success)
        {
            success = false;
            try
            {
                if (!IsAdlxDllAvailable())
                {
                    return false;
                }

                success = SetRSR(isEnabled);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calling SetRSR: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Safe wrapper for GetRSRSharpness that handles DLL load failures
        /// </summary>
        public static bool TryGetRSRSharpness(out int sharpness)
        {
            sharpness = -1;
            try
            {
                if (!IsAdlxDllAvailable())
                {
                    return false;
                }

                sharpness = GetRSRSharpness();
                return sharpness >= 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calling GetRSRSharpness: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Safe wrapper for SetRSRSharpness that handles DLL load failures
        /// </summary>
        public static bool TrySetRSRSharpness(int sharpness, out bool success)
        {
            success = false;
            try
            {
                if (!IsAdlxDllAvailable())
                {
                    return false;
                }

                // Validate sharpness range
                if (sharpness < 0 || sharpness > 100)
                {
                    System.Diagnostics.Debug.WriteLine($"Invalid sharpness value: {sharpness}. Must be 0-100.");
                    return false;
                }

                success = SetRSRSharpness(sharpness);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error calling SetRSRSharpness: {ex.Message}");
                return false;
            }
        }

        #endregion
    }
}
