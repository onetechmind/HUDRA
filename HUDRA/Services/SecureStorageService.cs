using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace HUDRA.Services
{
    /// <summary>
    /// Provides secure storage for sensitive data using Windows DPAPI.
    /// API keys are encrypted with the current user's credentials and stored locally.
    /// </summary>
    public class SecureStorageService
    {
        private const string API_KEY_FILENAME = "sgdb.dat";

        private static readonly string StoragePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HUDRA");

        /// <summary>
        /// Encrypts and saves an API key to local app data.
        /// </summary>
        /// <param name="apiKey">The API key to save</param>
        /// <exception cref="ArgumentException">Thrown if the key format is invalid</exception>
        public async Task SaveApiKeyAsync(string apiKey)
        {
            // Trim whitespace and newlines that may have been pasted
            apiKey = apiKey?.Trim().Replace("\r", "").Replace("\n", "") ?? string.Empty;

            if (!ValidateKeyFormat(apiKey))
                throw new ArgumentException("Invalid API key format. Key must be 32 hexadecimal characters.");

            try
            {
                // Ensure directory exists
                if (!Directory.Exists(StoragePath))
                {
                    Directory.CreateDirectory(StoragePath);
                }

                var protectedData = ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(apiKey),
                    optionalEntropy: null,
                    scope: DataProtectionScope.CurrentUser);

                var filePath = Path.Combine(StoragePath, API_KEY_FILENAME);
                await File.WriteAllBytesAsync(filePath, protectedData);

                System.Diagnostics.Debug.WriteLine("SecureStorageService: API key saved successfully");
            }
            catch (CryptographicException ex)
            {
                System.Diagnostics.Debug.WriteLine($"SecureStorageService: DPAPI encryption failed - {ex.Message}");
                throw new InvalidOperationException("Failed to encrypt API key. Secure storage may not be available.", ex);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SecureStorageService: Error saving API key - {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Retrieves and decrypts the stored API key.
        /// </summary>
        /// <returns>The decrypted API key, or null if not found or corrupted</returns>
        public async Task<string?> GetApiKeyAsync()
        {
            try
            {
                var filePath = Path.Combine(StoragePath, API_KEY_FILENAME);

                if (!File.Exists(filePath))
                {
                    return null;
                }

                var protectedData = await File.ReadAllBytesAsync(filePath);
                var decrypted = ProtectedData.Unprotect(
                    protectedData,
                    optionalEntropy: null,
                    scope: DataProtectionScope.CurrentUser);

                return Encoding.UTF8.GetString(decrypted);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (CryptographicException ex)
            {
                // File corrupted or encrypted by different user - delete and return null
                System.Diagnostics.Debug.WriteLine($"SecureStorageService: DPAPI decryption failed (corrupted?) - {ex.Message}");
                await DeleteApiKeyAsync();
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SecureStorageService: Error retrieving API key - {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Removes the stored API key.
        /// </summary>
        public async Task DeleteApiKeyAsync()
        {
            try
            {
                var filePath = Path.Combine(StoragePath, API_KEY_FILENAME);

                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    System.Diagnostics.Debug.WriteLine("SecureStorageService: API key deleted");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SecureStorageService: Error deleting API key - {ex.Message}");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Validates that a key is in the expected 32-character hexadecimal format.
        /// </summary>
        /// <param name="key">The key to validate</param>
        /// <returns>True if the key is valid, false otherwise</returns>
        public bool ValidateKeyFormat(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return false;

            // Trim whitespace for validation
            key = key.Trim();

            if (key.Length != 32)
                return false;

            return key.All(c => Uri.IsHexDigit(c));
        }

        /// <summary>
        /// Checks if an API key is currently stored.
        /// </summary>
        /// <returns>True if an API key exists, false otherwise</returns>
        public async Task<bool> HasApiKeyAsync()
        {
            var key = await GetApiKeyAsync();
            return !string.IsNullOrEmpty(key);
        }
    }
}
