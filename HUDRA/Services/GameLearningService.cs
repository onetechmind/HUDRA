using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HUDRA.Services
{
    public class GameLearningService
    {
        private readonly string _appDataPath;
        private readonly string _inclusionListPath;
        private readonly string _exclusionListPath;
        private HashSet<string> _inclusionList = new(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _exclusionList = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();

        public GameLearningService()
        {
            _appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HUDRA");

            _inclusionListPath = Path.Combine(_appDataPath, "game_inclusion_list.json");
            _exclusionListPath = Path.Combine(_appDataPath, "game_exclusion_list.json");

            LoadLists();
        }

        private void LoadLists()
        {
            try
            {
                if (!Directory.Exists(_appDataPath))
                {
                    Directory.CreateDirectory(_appDataPath);
                }

                // Load inclusion list
                if (File.Exists(_inclusionListPath))
                {
                    var inclusionJson = File.ReadAllText(_inclusionListPath);
                    var inclusionArray = JsonSerializer.Deserialize<string[]>(inclusionJson);
                    if (inclusionArray != null)
                    {
                        _inclusionList = new HashSet<string>(inclusionArray, StringComparer.OrdinalIgnoreCase);
                    }
                }

                // Load exclusion list
                if (File.Exists(_exclusionListPath))
                {
                    var exclusionJson = File.ReadAllText(_exclusionListPath);
                    var exclusionArray = JsonSerializer.Deserialize<string[]>(exclusionJson);
                    if (exclusionArray != null)
                    {
                        _exclusionList = new HashSet<string>(exclusionArray, StringComparer.OrdinalIgnoreCase);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading game lists: {ex.Message}");
            }
        }

        private void SaveLists()
        {
            try
            {
                lock (_lock)
                {
                    // Save inclusion list
                    var inclusionJson = JsonSerializer.Serialize(_inclusionList.ToArray(), new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    File.WriteAllText(_inclusionListPath, inclusionJson);

                    // Save exclusion list
                    var exclusionJson = JsonSerializer.Serialize(_exclusionList.ToArray(), new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    File.WriteAllText(_exclusionListPath, exclusionJson);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving game lists: {ex.Message}");
            }
        }

        public bool IsKnownGame(string processName)
        {
            lock (_lock)
            {
                return _inclusionList.Contains(processName);
            }
        }

        public bool IsKnownNonGame(string processName)
        {
            lock (_lock)
            {
                return _exclusionList.Contains(processName);
            }
        }

        public void LearnGame(string processName)
        {
            lock (_lock)
            {
                if (!_inclusionList.Contains(processName))
                {
                    _inclusionList.Add(processName);
                    // Remove from exclusion list if it was there (learning override)
                    _exclusionList.Remove(processName);
                    SaveLists();
                    System.Diagnostics.Debug.WriteLine($"Learned new game: {processName}");
                }
            }
        }

        public void LearnNonGame(string processName)
        {
            lock (_lock)
            {
                if (!_exclusionList.Contains(processName))
                {
                    _exclusionList.Add(processName);
                    // Remove from inclusion list if it was there (learning override)
                    _inclusionList.Remove(processName);
                    SaveLists();
                    System.Diagnostics.Debug.WriteLine($"Learned non-game: {processName}");
                }
            }
        }

        public int InclusionListCount => _inclusionList.Count;
        public int ExclusionListCount => _exclusionList.Count;
    }
}