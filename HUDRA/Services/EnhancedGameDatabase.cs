using HUDRA.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace HUDRA.Services
{
    /// <summary>
    /// JSON-based game library database. Stores games in human-readable JSON format
    /// at %LocalAppData%\HUDRA\game_library.json for easy manual editing during development.
    /// </summary>
    public class EnhancedGameDatabase : IDisposable
    {
        private readonly string _databasePath;
        private readonly SemaphoreSlim _fileLock = new(1, 1);
        private ConcurrentDictionary<string, DetectedGame> _games = new(StringComparer.OrdinalIgnoreCase);
        private bool _disposed = false;
        private bool _isDirty = false;
        private readonly Timer _autoSaveTimer;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        public EnhancedGameDatabase()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HUDRA");

            Directory.CreateDirectory(appDataPath);
            _databasePath = Path.Combine(appDataPath, "game_library.json");

            // Load existing data
            LoadFromDisk();

            // Auto-save every 30 seconds if dirty
            _autoSaveTimer = new Timer(_ => SaveIfDirtyAsync().ConfigureAwait(false), null,
                TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

            System.Diagnostics.Debug.WriteLine($"Enhanced game database initialized: {_databasePath} ({_games.Count} games)");
        }

        #region Disk I/O

        private void LoadFromDisk()
        {
            try
            {
                if (!File.Exists(_databasePath))
                {
                    _games = new ConcurrentDictionary<string, DetectedGame>(StringComparer.OrdinalIgnoreCase);
                    return;
                }

                var json = File.ReadAllText(_databasePath);

                if (string.IsNullOrWhiteSpace(json))
                {
                    _games = new ConcurrentDictionary<string, DetectedGame>(StringComparer.OrdinalIgnoreCase);
                    return;
                }

                var games = JsonSerializer.Deserialize<List<DetectedGame>>(json, _jsonOptions);

                if (games != null)
                {
                    _games = new ConcurrentDictionary<string, DetectedGame>(
                        games.Where(g => g != null && !string.IsNullOrEmpty(g.ProcessName))
                             .ToDictionary(g => g.ProcessName, g => g, StringComparer.OrdinalIgnoreCase));
                }

                System.Diagnostics.Debug.WriteLine($"Loaded {_games.Count} games from {_databasePath}");
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"JSON parse error, starting fresh: {ex.Message}");
                _games = new ConcurrentDictionary<string, DetectedGame>(StringComparer.OrdinalIgnoreCase);
                BackupCorruptedFile();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading game database: {ex.Message}");
                _games = new ConcurrentDictionary<string, DetectedGame>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void BackupCorruptedFile()
        {
            try
            {
                if (File.Exists(_databasePath))
                {
                    var backupPath = _databasePath + $".corrupted.{DateTime.Now:yyyyMMdd_HHmmss}";
                    File.Move(_databasePath, backupPath);
                    System.Diagnostics.Debug.WriteLine($"Corrupted database backed up to: {backupPath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to backup corrupted file: {ex.Message}");
            }
        }

        private void SaveToDisk()
        {
            if (_disposed) return;

            _fileLock.Wait();
            try
            {
                var gamesList = _games.Values
                    .Where(g => g != null)
                    .OrderBy(g => g.DisplayName ?? g.ProcessName)
                    .ToList();

                var json = JsonSerializer.Serialize(gamesList, _jsonOptions);

                // Atomic write: temp file then rename
                var tempPath = _databasePath + ".tmp";
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, _databasePath, overwrite: true);

                _isDirty = false;
                System.Diagnostics.Debug.WriteLine($"Saved {gamesList.Count} games to {_databasePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving game database: {ex.Message}");
            }
            finally
            {
                _fileLock.Release();
            }
        }

        private async Task SaveIfDirtyAsync()
        {
            if (_isDirty && !_disposed)
            {
                await Task.Run(() => SaveToDisk());
            }
        }

        /// <summary>
        /// Forces immediate save to disk. Call on application shutdown.
        /// </summary>
        public void Flush()
        {
            if (_isDirty && !_disposed)
            {
                SaveToDisk();
            }
        }

        #endregion

        #region CRUD Operations

        public void SaveGame(DetectedGame game)
        {
            if (_disposed) return;
            if (game == null || string.IsNullOrEmpty(game.ProcessName)) return;

            try
            {
                if (_games.TryGetValue(game.ProcessName, out var existing))
                {
                    // Preserve FirstDetected from existing record
                    game.FirstDetected = existing.FirstDetected;
                    game.LastDetected = DateTime.Now;
                }
                else
                {
                    game.FirstDetected = DateTime.Now;
                    game.LastDetected = DateTime.Now;
                }

                _games[game.ProcessName] = game;
                _isDirty = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving game {game.ProcessName}: {ex.Message}");
                throw new InvalidOperationException($"Failed to save game {game.ProcessName}", ex);
            }
        }

        public void SaveGames(IEnumerable<DetectedGame> games)
        {
            if (_disposed) return;

            try
            {
                var gameList = games?.Where(g => g != null && !string.IsNullOrEmpty(g.ProcessName)).ToList();
                if (gameList == null || !gameList.Any()) return;

                foreach (var game in gameList)
                {
                    if (_games.TryGetValue(game.ProcessName, out var existing))
                    {
                        game.FirstDetected = existing.FirstDetected;
                        game.LastDetected = DateTime.Now;
                    }
                    else
                    {
                        game.FirstDetected = DateTime.Now;
                        game.LastDetected = DateTime.Now;
                    }

                    _games[game.ProcessName] = game;
                }

                _isDirty = true;
                // Immediate save for batch operations (matches LiteDB behavior)
                SaveToDisk();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving games batch: {ex.Message}");
                // Fallback to individual saves
                foreach (var game in games ?? Enumerable.Empty<DetectedGame>())
                {
                    try { SaveGame(game); }
                    catch (Exception individualEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error saving {game?.ProcessName}: {individualEx.Message}");
                    }
                }
            }
        }

        public DetectedGame? GetGame(string processName)
        {
            if (_disposed) return null;
            if (string.IsNullOrEmpty(processName)) return null;

            _games.TryGetValue(processName, out var game);
            return game;
        }

        public IEnumerable<DetectedGame> GetAllGames()
        {
            if (_disposed) return Enumerable.Empty<DetectedGame>();

            try
            {
                return _games.Values.Where(g => g != null).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting all games: {ex.Message}");
                return Enumerable.Empty<DetectedGame>();
            }
        }

        public async Task<IEnumerable<DetectedGame>> GetAllGamesAsync()
        {
            if (_disposed) return Enumerable.Empty<DetectedGame>();

            // Data is in memory - maintain async signature for API compatibility
            // Using Task.FromResult to match original LiteDB behavior that used Task.Run
            return await Task.FromResult(GetAllGames());
        }

        public IEnumerable<DetectedGame> GetGamesBySource(GameSource source)
        {
            if (_disposed) return Enumerable.Empty<DetectedGame>();

            try
            {
                return _games.Values
                    .Where(g => g != null && g.Source == source)
                    .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting games by source {source}: {ex.Message}");
                return Enumerable.Empty<DetectedGame>();
            }
        }

        public bool DeleteGame(string processName)
        {
            if (_disposed) return false;
            if (string.IsNullOrEmpty(processName)) return false;

            try
            {
                var removed = _games.TryRemove(processName, out _);
                if (removed) _isDirty = true;
                return removed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting game {processName}: {ex.Message}");
                return false;
            }
        }

        public bool UpdateGameDisplayName(string processName, string newDisplayName)
        {
            if (_disposed) return false;
            if (string.IsNullOrEmpty(processName)) return false;

            try
            {
                if (_games.TryGetValue(processName, out var game))
                {
                    game.DisplayName = newDisplayName;
                    game.LastDetected = DateTime.Now;
                    _isDirty = true;
                    return true;
                }

                System.Diagnostics.Debug.WriteLine($"Game {processName} not found for DisplayName update");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating DisplayName for {processName}: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Bulk Operations

        public int ClearXboxGames()
        {
            if (_disposed) return 0;

            try
            {
                var xboxGames = _games.Values
                    .Where(g => g != null && g.Source == GameSource.Xbox)
                    .Select(g => g.ProcessName)
                    .ToList();

                foreach (var processName in xboxGames)
                {
                    _games.TryRemove(processName, out _);
                }

                if (xboxGames.Count > 0)
                {
                    _isDirty = true;
                    SaveToDisk(); // Immediate save for bulk operations
                }

                return xboxGames.Count;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing Xbox games: {ex.Message}");
                return 0;
            }
        }

        public void ClearDatabase()
        {
            if (_disposed) return;

            try
            {
                _games.Clear();
                _isDirty = true;
                SaveToDisk(); // Immediate save
                System.Diagnostics.Debug.WriteLine("Enhanced game database cleared");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing database: {ex.Message}");
            }
        }

        #endregion

        #region Statistics

        public DatabaseStats GetDatabaseStats()
        {
            if (_disposed) return new DatabaseStats();

            try
            {
                var allGames = _games.Values.Where(g => g != null).ToList();
                return new DatabaseStats
                {
                    TotalGames = allGames.Count,
                    GamesBySource = allGames
                        .GroupBy(g => g.Source)
                        .ToDictionary(g => g.Key, g => g.Count()),
                    DatabaseSizeBytes = File.Exists(_databasePath)
                        ? new FileInfo(_databasePath).Length
                        : 0,
                    LastUpdated = allGames.Any()
                        ? allGames.Max(g => g.LastDetected)
                        : DateTime.MinValue
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting database stats: {ex.Message}");
                return new DatabaseStats();
            }
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                try
                {
                    _autoSaveTimer?.Dispose();
                    Flush(); // Final save
                    _fileLock?.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error disposing database: {ex.Message}");
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// Database statistics - unchanged from LiteDB implementation.
    /// </summary>
    public class DatabaseStats
    {
        public int TotalGames { get; set; }
        public Dictionary<GameSource, int> GamesBySource { get; set; } = new();
        public long DatabaseSizeBytes { get; set; }
        public DateTime LastUpdated { get; set; }

        public string GetFormattedSize()
        {
            if (DatabaseSizeBytes < 1024) return $"{DatabaseSizeBytes} B";
            if (DatabaseSizeBytes < 1024 * 1024) return $"{DatabaseSizeBytes / 1024.0:F1} KB";
            return $"{DatabaseSizeBytes / (1024.0 * 1024.0):F1} MB";
        }
    }
}
