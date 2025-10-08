namespace Waterjam.Core.Services.Platform;

/// <summary>
/// Composite adapter exposing platform facilities used by the game.
/// </summary>
public interface IPlatformAdapter
{
    /// <summary>
    /// Human readable platform name (e.g., Steam, Epic, P2P, Null).
    /// </summary>
    string PlatformName { get; }

    /// <summary>
    /// Cloud storage abstraction (may be a no-op if unavailable).
    /// </summary>
    ICloudStorage Cloud { get; }

    /// <summary>
    /// Achievements/statistics abstraction (may be a no-op if unavailable).
    /// </summary>
    IAchievementPlatform Achievements { get; }
}


