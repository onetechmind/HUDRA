using HUDRA.Models;
using LiteDB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HUDRA.Services
{
    public class EnhancedGameDatabase : IDisposable
    {
        private readonly LiteDatabase _database;
        private readonly ILiteCollection<DetectedGame> _games;
        private readonly string _databasePath;
        private bool _disposed = false;

        public EnhancedGameDatabase()
        {
            try
            {
                // Create database in HUDRA AppData folder
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HUDRA");

                if (!Directory.Exists(appDataPath))
                {
                    Directory.CreateDirectory(appDataPath);
                }

                _databasePath = Path.Combine(appDataPath, "enhanced_games.db");
                
                // Initialize LiteDB with connection string for better performance
                var connectionString = new ConnectionString
                {
                    Filename = _databasePath,
                    Connection = ConnectionType.Shared,
                    Upgrade = true
                };

                _database = new LiteDatabase(connectionString);
                _games = _database.GetCollection<DetectedGame>("games");

                // Ensure indexes are created
                _games.EnsureIndex(x => x.ProcessName, unique: true);
                _games.EnsureIndex(x => x.Source);
                _games.EnsureIndex(x => x.LastDetected);

                System.Diagnostics.Debug.WriteLine($"Enhanced game database initialized: {_databasePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing enhanced game database: {ex.Message}");
                throw;
            }
        }

        public void SaveGame(DetectedGame game)
        {
            if (_disposed) return;

            try
            {
                var existingGame = _games.FindById(game.ProcessName);
                if (existingGame != null)
                {
                    // Update existing game, preserve FirstDetected
                    game.FirstDetected = existingGame.FirstDetected;
                    game.LastDetected = DateTime.Now;
                    _games.Update(game);
                }
                else
                {
                    // New game
                    game.FirstDetected = DateTime.Now;
                    game.LastDetected = DateTime.Now;
                    _games.Insert(game);
                }
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
                var gameList = games.ToList();
                if (!gameList.Any()) return;

                // Prepare games for batch upsert with proper timestamps
                foreach (var game in gameList)
                {
                    var existingGame = _games.FindById(game.ProcessName);
                    if (existingGame != null)
                    {
                        game.FirstDetected = existingGame.FirstDetected;
                        game.LastDetected = DateTime.Now;
                    }
                    else
                    {
                        game.FirstDetected = DateTime.Now;
                        game.LastDetected = DateTime.Now;
                    }
                }

                _games.Upsert(gameList);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving games batch: {ex.Message}");
                // Fallback to individual saves if batch fails
                foreach (var game in games)
                {
                    try
                    {
                        SaveGame(game);
                    }
                    catch (Exception individualEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error saving individual game {game.ProcessName}: {individualEx.Message}");
                    }
                }
            }
        }

        public DetectedGame? GetGame(string processName)
        {
            if (_disposed) return null;

            try
            {
                return _games.FindById(processName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting game {processName}: {ex.Message}");
                return null;
            }
        }

        public IEnumerable<DetectedGame> GetAllGames()
        {
            if (_disposed) return Enumerable.Empty<DetectedGame>();

            try
            {
                return _games.FindAll().ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting all games: {ex.Message}");
                return Enumerable.Empty<DetectedGame>();
            }
        }

        public IEnumerable<DetectedGame> GetGamesBySource(GameSource source)
        {
            if (_disposed) return Enumerable.Empty<DetectedGame>();

            try
            {
                return _games.Find(x => x.Source == source).ToList();
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

            try
            {
                return _games.Delete(processName);
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

            try
            {
                var existingGame = _games.FindById(processName);
                if (existingGame != null)
                {
                    existingGame.DisplayName = newDisplayName;
                    existingGame.LastDetected = DateTime.Now;
                    _games.Update(existingGame);
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Game {processName} not found for DisplayName update");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating DisplayName for game {processName}: {ex.Message}");
                return false;
            }
        }

        public DatabaseStats GetDatabaseStats()
        {
            if (_disposed) return new DatabaseStats();

            try
            {
                var allGames = GetAllGames().ToList();
                var stats = new DatabaseStats
                {
                    TotalGames = allGames.Count,
                    GamesBySource = allGames.GroupBy(g => g.Source)
                                          .ToDictionary(g => g.Key, g => g.Count()),
                    DatabaseSizeBytes = new FileInfo(_databasePath).Length,
                    LastUpdated = allGames.Any() ? allGames.Max(g => g.LastDetected) : DateTime.MinValue
                };

                return stats;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting database stats: {ex.Message}");
                return new DatabaseStats();
            }
        }

        public void ClearDatabase()
        {
            if (_disposed) return;

            try
            {
                _games.DeleteAll();
                System.Diagnostics.Debug.WriteLine("Enhanced game database cleared");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing database: {ex.Message}");
            }
        }

        public int ClearXboxGames()
        {
            if (_disposed) return 0;

            try
            {
                var deletedCount = _games.DeleteMany(x => x.Source == GameSource.Xbox);
                return deletedCount;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing Xbox games: {ex.Message}");
                return 0;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    _database?.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error disposing database: {ex.Message}");
                }
                finally
                {
                    _disposed = true;
                }
            }
        }
    }

    public class DatabaseStats
    {
        public int TotalGames { get; set; }
        public Dictionary<GameSource, int> GamesBySource { get; set; } = new();
        public long DatabaseSizeBytes { get; set; }
        public DateTime LastUpdated { get; set; }

        public string GetFormattedSize()
        {
            if (DatabaseSizeBytes < 1024) return $"{DatabaseSizeBytes} B";
            if (DatabaseSizeBytes < 1024 * 1024) return $"{DatabaseSizeBytes / 1024:F1} KB";
            return $"{DatabaseSizeBytes / (1024 * 1024):F1} MB";
        }
    }
}