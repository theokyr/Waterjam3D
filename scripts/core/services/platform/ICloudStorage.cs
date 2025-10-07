using System;

namespace Waterjam.Core.Services.Platform;

/// <summary>
/// Abstraction for platform cloud storage (e.g., Steam Cloud, Epic, PSN, or custom backend).
/// </summary>
public interface ICloudStorage
{
    /// <summary>
    /// Indicates whether cloud storage is currently available and usable.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Saves a binary payload to the cloud under a logical filename.
    /// </summary>
    /// <param name="filename">Logical name of the file (no directories).</param>
    /// <param name="data">Binary payload to persist.</param>
    /// <returns>True on success, false otherwise.</returns>
    bool Save(string filename, byte[] data);

    /// <summary>
    /// Loads a binary payload from the cloud by logical filename.
    /// </summary>
    /// <param name="filename">Logical name of the file (no directories).</param>
    /// <returns>Binary payload or null if missing/unavailable.</returns>
    byte[] Load(string filename);
}


