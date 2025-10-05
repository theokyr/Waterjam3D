using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace Waterjam.Core.Services.Network;

/// <summary>
/// Interface for mod repository providers (Steam Workshop, GitHub, mod.io, etc.)
/// Allows downloading mods from multiple sources.
/// </summary>
public interface IModRepository
{
    /// <summary>
    /// Unique identifier for this repository type
    /// </summary>
    string RepositoryId { get; }

    /// <summary>
    /// Display name for UI
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Priority (lower = try first)
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Check if this repository can handle a given mod requirement
    /// </summary>
    bool CanHandle(ModRequirementInfo requirement);

    /// <summary>
    /// Download a mod from this repository
    /// </summary>
    Task<ModDownloadResult> DownloadModAsync(ModRequirementInfo requirement, IProgress<ModDownloadProgress> progress);

    /// <summary>
    /// Verify downloaded mod integrity
    /// </summary>
    Task<bool> VerifyIntegrityAsync(string localPath, byte[] expectedChecksum);

    /// <summary>
    /// Get mod metadata without downloading
    /// </summary>
    Task<ModMetadata> GetMetadataAsync(string modId);
}

/// <summary>
/// Information about a required mod
/// </summary>
public class ModRequirementInfo
{
    public string Id { get; set; }
    public string Version { get; set; }
    public byte[] Checksum { get; set; }
    public string DownloadUrl { get; set; }
    public ulong SizeBytes { get; set; }

    // Repository hints
    public string RepositoryType { get; set; } // "steam", "github", "modio", "http"
    public string RepositoryId { get; set; } // Workshop ID, repo name, etc.
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// Result of mod download operation
/// </summary>
public class ModDownloadResult
{
    public bool Success { get; set; }
    public string LocalPath { get; set; }
    public string Error { get; set; }
    public byte[] ActualChecksum { get; set; }
    public long BytesDownloaded { get; set; }
}

/// <summary>
/// Download progress information
/// </summary>
public struct ModDownloadProgress
{
    public string ModId;
    public long BytesDownloaded;
    public long TotalBytes;
    public float Percentage => TotalBytes > 0 ? (float)BytesDownloaded / TotalBytes * 100 : 0;
    public string Status; // "Downloading", "Extracting", "Verifying"
}

/// <summary>
/// Mod metadata
/// </summary>
public class ModMetadata
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Version { get; set; }
    public string Author { get; set; }
    public string Description { get; set; }
    public ulong SizeBytes { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string[] Tags { get; set; }
    public int Downloads { get; set; }
    public float Rating { get; set; }
}