using HUDRA.Models;
using SteamGridDB;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace HUDRA.Services
{
    public class SteamGridDbArtworkService : IDisposable
    {
        private readonly SteamGridDbClient _client;
        private readonly string _artworkDirectory;
        private readonly HttpClient _httpClient;
        private bool _disposed = false;

        public SteamGridDbArtworkService(string apiKey)
        {
            _client = new SteamGridDbClient(apiKey);
            _httpClient = new HttpClient();

            // Create artwork directory in HUDRA AppData folder
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HUDRA");

            _artworkDirectory = Path.Combine(appDataPath, "artwork");

            if (!Directory.Exists(_artworkDirectory))
            {
                Directory.CreateDirectory(_artworkDirectory);
                System.Diagnostics.Debug.WriteLine($"SteamGridDB: Created artwork directory: {_artworkDirectory}");
            }
        }

        /// <summary>
        /// Download artwork for a game if it doesn't already have it
        /// </summary>
        public async Task<string?> DownloadArtworkAsync(DetectedGame game)
        {
            if (_disposed) return null;

            try
            {
                // Skip if artwork already exists
                if (!string.IsNullOrEmpty(game.ArtworkPath) && File.Exists(game.ArtworkPath))
                {
                    System.Diagnostics.Debug.WriteLine($"SteamGridDB: Artwork already exists for {game.DisplayName}");
                    return game.ArtworkPath;
                }

                System.Diagnostics.Debug.WriteLine($"SteamGridDB: Searching for artwork for {game.DisplayName}");

                // Search for the game by name
                var searchResults = await _client.SearchGamesAsync(game.DisplayName);

                if (searchResults == null || !searchResults.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"SteamGridDB: No games found for {game.DisplayName}");
                    return null;
                }

                // Get the first matching game
                var steamGridGame = searchResults.First();
                System.Diagnostics.Debug.WriteLine($"SteamGridDB: Found game: {steamGridGame.Name} (ID: {steamGridGame.GameId})");

                // Get grid images for this game
                var grids = await _client.GetGridsByGameIdAsync(steamGridGame.GameId);

                if (grids == null || !grids.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"SteamGridDB: No grid images found for {steamGridGame.Name}");
                    return null;
                }

                // Get the first grid image
                var gridImage = grids.First();
                System.Diagnostics.Debug.WriteLine($"SteamGridDB: Downloading grid image from {gridImage.Url}");

                // Download the image
                var imageBytes = await _httpClient.GetByteArrayAsync(gridImage.Url);

                // Generate filename based on game process name (sanitize for filesystem)
                var safeFileName = SanitizeFileName(game.ProcessName);
                var extension = Path.GetExtension(gridImage.Url.ToString()) ?? ".png";
                var fileName = $"{safeFileName}{extension}";
                var filePath = Path.Combine(_artworkDirectory, fileName);

                // Save the image
                await File.WriteAllBytesAsync(filePath, imageBytes);
                System.Diagnostics.Debug.WriteLine($"SteamGridDB: Saved artwork to {filePath}");

                return filePath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SteamGridDB: Error downloading artwork for {game.DisplayName}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Download artwork for multiple games
        /// </summary>
        public async Task DownloadArtworkForGamesAsync(System.Collections.Generic.IEnumerable<DetectedGame> games,
            EnhancedGameDatabase database,
            Action<string>? progressCallback = null)
        {
            if (_disposed) return;

            var gamesList = games.ToList();
            var total = gamesList.Count;
            var current = 0;

            foreach (var game in gamesList)
            {
                current++;
                progressCallback?.Invoke($"Downloading artwork {current}/{total}: {game.DisplayName}");

                try
                {
                    var artworkPath = await DownloadArtworkAsync(game);

                    if (!string.IsNullOrEmpty(artworkPath))
                    {
                        // Update the game's artwork path in the database
                        game.ArtworkPath = artworkPath;
                        database.SaveGame(game);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SteamGridDB: Failed to download artwork for {game.DisplayName}: {ex.Message}");
                }

                // Small delay to avoid hitting rate limits
                await Task.Delay(100);
            }

            progressCallback?.Invoke($"Artwork download complete: {current}/{total} games processed");
        }

        /// <summary>
        /// Sanitize filename for filesystem
        /// </summary>
        private string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = new string(fileName.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
            return sanitized;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient?.Dispose();
                _disposed = true;
            }
        }
    }
}
