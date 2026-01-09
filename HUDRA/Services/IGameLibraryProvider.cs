using HUDRA.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HUDRA.Services
{
    public interface IGameLibraryProvider
    {
        string ProviderName { get; }
        GameSource GameSource { get; }
        bool IsAvailable { get; }

        Task<Dictionary<string, DetectedGame>> GetGamesAsync();
        event EventHandler<string>? ScanProgressChanged;

        /// <summary>
        /// Clears any cached data to force a fresh scan on next GetGamesAsync call.
        /// Used for manual rescans to detect newly installed games.
        /// </summary>
        void ClearCache();
    }
}