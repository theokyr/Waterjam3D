using System;

namespace Waterjam.Core.Services.Platform;

/// <summary>
/// Abstraction for platform achievements/stats APIs.
/// </summary>
public interface IAchievementPlatform
{
    /// <summary>
    /// Indicates whether the achievements platform is available.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Unlocks an achievement by its platform key/ID.
    /// </summary>
    /// <param name="achievementId">Platform-specific achievement identifier.</param>
    /// <returns>True if request accepted.</returns>
    bool Unlock(string achievementId);

    /// <summary>
    /// Sets an integer statistic.
    /// </summary>
    /// <param name="statName">Platform-specific stat key.</param>
    /// <param name="value">New value.</param>
    /// <returns>True on success.</returns>
    bool SetStat(string statName, int value);
}


