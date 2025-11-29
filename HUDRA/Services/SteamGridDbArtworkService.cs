using HUDRA.Models;
using craftersmine.SteamGridDBNet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HUDRA.Services
{
    public class SteamGridDbArtworkService : IDisposable
    {
        private readonly SteamGridDb _client;
        private readonly string _artworkDirectory;
        private readonly HttpClient _httpClient;
        private bool _disposed = false;

        // Regex to strip special characters and punctuation from game names for searching and matching
        // Includes trademark symbols AND all punctuation (colons, dashes, etc.) for consistent normalization
        private static readonly Regex SpecialCharactersRegex = new Regex(
            @"[™®©℠\u2122\u00AE\u00A9\u2120]|[^\w\s]",
            RegexOptions.Compiled);

        // Minimum score threshold for accepting a match (0-100)
        private const int MinimumMatchScore = 50;

        public SteamGridDbArtworkService(string apiKey)
        {
            _client = new SteamGridDb(apiKey);
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

                // Clean the search term by removing trademark/copyright symbols
                var cleanedSearchTerm = NormalizeName(game.DisplayName);
                System.Diagnostics.Debug.WriteLine($"SteamGridDB: Normalized search term: {cleanedSearchTerm}");

                // Search for the game by name
                var searchResults = await _client.SearchForGamesAsync(cleanedSearchTerm);

                if (searchResults == null || !searchResults.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"SteamGridDB: No games found for {game.DisplayName}");
                    return null;
                }

                // Score all results and find the best match
                var scoredResults = searchResults
                    .Select(result => new { Game = result, Score = ScoreMatch(game.DisplayName, result.Name) })
                    .OrderByDescending(x => x.Score)
                    .ToList();

                // Log all results with scores for debugging
                foreach (var scored in scoredResults.Take(5))
                {
                    System.Diagnostics.Debug.WriteLine($"SteamGridDB: Result '{scored.Game.Name}' (ID: {scored.Game.Id}) - Score: {scored.Score}");
                }

                var bestMatch = scoredResults.FirstOrDefault();

                // Reject if no good match found
                if (bestMatch == null || bestMatch.Score < MinimumMatchScore)
                {
                    System.Diagnostics.Debug.WriteLine($"SteamGridDB: No suitable match found for {game.DisplayName} (best score: {bestMatch?.Score ?? 0}, minimum required: {MinimumMatchScore})");
                    return null;
                }

                var steamGridGame = bestMatch.Game;
                System.Diagnostics.Debug.WriteLine($"SteamGridDB: Selected best match: {steamGridGame.Name} (ID: {steamGridGame.Id}, Score: {bestMatch.Score})");

                // Get grid images for this game
                var grids = await _client.GetGridsByGameIdAsync(steamGridGame.Id);

                if (grids == null || !grids.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"SteamGridDB: No grid images found for {steamGridGame.Name}");
                    return null;
                }

                // Select the best quality artwork by preferring higher resolution images
                // This should generally give us the official box art
                var gridImage = grids
                    .OrderByDescending(g => g.Width * g.Height) // Highest resolution first
                    .FirstOrDefault();

                // Fallback to any grid if somehow we got nothing
                if (gridImage == null)
                {
                    gridImage = grids.First();
                }

                System.Diagnostics.Debug.WriteLine($"SteamGridDB: Selected grid - Resolution: {gridImage.Width}x{gridImage.Height}");
                System.Diagnostics.Debug.WriteLine($"SteamGridDB: Downloading grid image from {gridImage.FullImageUrl}");

                // Download the image
                var imageBytes = await _httpClient.GetByteArrayAsync(gridImage.FullImageUrl);

                // Generate filename based on game process name (sanitize for filesystem)
                var safeFileName = SanitizeFileName(game.ProcessName);
                var extension = Path.GetExtension(gridImage.FullImageUrl.ToString()) ?? ".png";
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
        /// Normalize a game name for comparison by removing special characters and standardizing format
        /// </summary>
        private string NormalizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            // Strip trademark/copyright symbols
            var cleaned = SpecialCharactersRegex.Replace(name, "");

            // Normalize whitespace and convert to lowercase for comparison
            return Regex.Replace(cleaned, @"\s+", " ").Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Score how well a search result matches the original game name (0-100)
        /// </summary>
        private int ScoreMatch(string searchName, string resultName)
        {
            var normalizedSearch = NormalizeName(searchName);
            var normalizedResult = NormalizeName(resultName);

            if (string.IsNullOrEmpty(normalizedSearch) || string.IsNullOrEmpty(normalizedResult))
                return 0;

            // Exact match after normalization
            if (normalizedSearch == normalizedResult) return 100;

            // Result starts with search term (e.g., "Stellar Blade" matches "Stellar Blade Deluxe")
            if (normalizedResult.StartsWith(normalizedSearch)) return 90;

            // Search term starts with result (e.g., searching "Stellar Blade Deluxe" matches "Stellar Blade")
            if (normalizedSearch.StartsWith(normalizedResult)) return 85;

            // Result contains search term
            if (normalizedResult.Contains(normalizedSearch)) return 70;

            // Search term contains result
            if (normalizedSearch.Contains(normalizedResult)) return 60;

            // Word-based matching - count shared words
            var searchWords = normalizedSearch.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var resultWords = normalizedResult.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var sharedWords = searchWords.Intersect(resultWords).Count();

            if (sharedWords > 0)
            {
                // Score based on percentage of words matched (max 50 for partial word matches)
                var maxWords = Math.Max(searchWords.Length, resultWords.Length);
                return (int)(50.0 * sharedWords / maxWords);
            }

            return 0; // No meaningful match
        }

        /// <summary>
        /// Get multiple artwork options from SteamGridDB for the user to choose from
        /// </summary>
        public async Task<List<SteamGridDbResult>?> GetArtworkOptionsAsync(string gameName, int maxResults = 10)
        {
            if (_disposed) return null;

            try
            {
                System.Diagnostics.Debug.WriteLine($"SteamGridDB: Fetching artwork options for {gameName}");

                // Clean the search term
                var cleanedSearchTerm = NormalizeName(gameName);
                System.Diagnostics.Debug.WriteLine($"SteamGridDB: Normalized search term: {cleanedSearchTerm}");

                // Search for the game
                var searchResults = await _client.SearchForGamesAsync(cleanedSearchTerm);

                if (searchResults == null || !searchResults.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"SteamGridDB: No games found for {gameName}");
                    return null;
                }

                // Score and find best match
                var scoredResults = searchResults
                    .Select(result => new { Game = result, Score = ScoreMatch(gameName, result.Name) })
                    .OrderByDescending(x => x.Score)
                    .ToList();

                var bestMatch = scoredResults.FirstOrDefault();
                if (bestMatch == null || bestMatch.Score < MinimumMatchScore)
                {
                    System.Diagnostics.Debug.WriteLine($"SteamGridDB: No suitable match found for {gameName}");
                    return null;
                }

                var steamGridGame = bestMatch.Game;
                System.Diagnostics.Debug.WriteLine($"SteamGridDB: Best match: {steamGridGame.Name} (Score: {bestMatch.Score})");

                // Get grid images for this game
                var grids = await _client.GetGridsByGameIdAsync(steamGridGame.Id);

                if (grids == null || !grids.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"SteamGridDB: No grid images found for {steamGridGame.Name}");
                    return null;
                }

                // Take up to maxResults, prioritizing higher resolution
                var selectedGrids = grids
                    .OrderByDescending(g => g.Width * g.Height)
                    .Take(maxResults)
                    .ToList();

                // Create temp directory for previews
                var tempDir = Path.Combine(Path.GetTempPath(), "HUDRA_SGDB");
                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                }

                // Download images to temp folder
                var results = new List<SteamGridDbResult>();
                for (int i = 0; i < selectedGrids.Count; i++)
                {
                    var grid = selectedGrids[i];
                    try
                    {
                        var imageBytes = await _httpClient.GetByteArrayAsync(grid.FullImageUrl);
                        var extension = Path.GetExtension(grid.FullImageUrl.ToString()) ?? ".png";
                        var tempFileName = $"{SanitizeFileName(steamGridGame.Name)}_{i}{extension}";
                        var tempFilePath = Path.Combine(tempDir, tempFileName);

                        await File.WriteAllBytesAsync(tempFilePath, imageBytes);

                        results.Add(new SteamGridDbResult
                        {
                            TempFilePath = tempFilePath,
                            FullImageUrl = grid.FullImageUrl.ToString(),
                            Width = grid.Width,
                            Height = grid.Height
                        });

                        System.Diagnostics.Debug.WriteLine($"SteamGridDB: Downloaded preview {i + 1}/{selectedGrids.Count}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"SteamGridDB: Failed to download grid {i}: {ex.Message}");
                    }
                }

                return results;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SteamGridDB: Error fetching artwork options: {ex.Message}");
                return null;
            }
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

    public class SteamGridDbResult
    {
        public string TempFilePath { get; set; } = string.Empty;
        public string FullImageUrl { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
