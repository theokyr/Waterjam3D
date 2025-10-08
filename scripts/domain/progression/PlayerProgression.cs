using System;
using System.Collections.Generic;

namespace Waterjam.Domain.Progression;

/// <summary>
/// Represents a player's progression data including currency, unlocks, and achievements.
/// This data is persisted locally and can be synced with online services like Steam.
/// </summary>
public class PlayerProgression : IEquatable<PlayerProgression>
{
    /// <summary>
    /// Unique identifier for this progression data.
    /// </summary>
    public string PlayerId { get; set; }

    /// <summary>
    /// Current currency amount.
    /// </summary>
    public int Currency { get; set; }

    /// <summary>
    /// Total currency earned across all play sessions.
    /// </summary>
    public int TotalCurrencyEarned { get; set; }

    /// <summary>
    /// Current experience points.
    /// </summary>
    public int Experience { get; set; }

    /// <summary>
    /// Player level.
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Unlocked items/abilities/characters.
    /// </summary>
    public HashSet<string> UnlockedItems { get; set; } = new();

    /// <summary>
    /// Completed achievements.
    /// </summary>
    public HashSet<string> CompletedAchievements { get; set; } = new();

    /// <summary>
    /// Game statistics.
    /// </summary>
    public Dictionary<string, int> Statistics { get; set; } = new();

    /// <summary>
    /// When this progression data was first created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When this progression data was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Version of the progression data format for migration purposes.
    /// </summary>
    public int DataVersion { get; set; } = 1;

    /// <summary>
    /// Whether this progression data has been synced with online services.
    /// </summary>
    public bool IsSyncedOnline { get; set; }

    /// <summary>
    /// Creates a new player progression instance.
    /// </summary>
    public PlayerProgression(string playerId)
    {
        PlayerId = playerId ?? throw new ArgumentNullException(nameof(playerId));
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
        Currency = 0;
        Experience = 0;
        Level = 1;
    }

    /// <summary>
    /// Adds currency to the player's balance.
    /// </summary>
    public void AddCurrency(int amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Cannot add negative currency");

        Currency += amount;
        TotalCurrencyEarned += amount;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Spends currency if the player has enough.
    /// </summary>
    /// <returns>True if the purchase was successful, false if insufficient funds.</returns>
    public bool SpendCurrency(int amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Cannot spend negative currency");

        if (Currency < amount)
            return false;

        Currency -= amount;
        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// Adds experience points.
    /// </summary>
    public void AddExperience(int amount)
    {
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Cannot add negative experience");

        Experience += amount;

        // Simple level calculation (could be made more sophisticated)
        var newLevel = CalculateLevelFromExperience(Experience);
        if (newLevel > Level)
        {
            Level = newLevel;
        }

        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Unlocks an item for the player.
    /// </summary>
    public void UnlockItem(string itemId)
    {
        if (string.IsNullOrEmpty(itemId))
            throw new ArgumentNullException(nameof(itemId));

        UnlockedItems.Add(itemId);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Checks if an item is unlocked.
    /// </summary>
    public bool IsItemUnlocked(string itemId)
    {
        return UnlockedItems.Contains(itemId);
    }

    /// <summary>
    /// Completes an achievement for the player.
    /// </summary>
    public void CompleteAchievement(string achievementId)
    {
        if (string.IsNullOrEmpty(achievementId))
            throw new ArgumentNullException(nameof(achievementId));

        CompletedAchievements.Add(achievementId);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Checks if an achievement is completed.
    /// </summary>
    public bool IsAchievementCompleted(string achievementId)
    {
        return CompletedAchievements.Contains(achievementId);
    }

    /// <summary>
    /// Updates a statistic.
    /// </summary>
    public void UpdateStatistic(string statName, int value)
    {
        if (string.IsNullOrEmpty(statName))
            throw new ArgumentNullException(nameof(statName));

        Statistics[statName] = value;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Increments a statistic by a given amount.
    /// </summary>
    public void IncrementStatistic(string statName, int increment = 1)
    {
        if (string.IsNullOrEmpty(statName))
            throw new ArgumentNullException(nameof(statName));

        Statistics.TryGetValue(statName, out var currentValue);
        Statistics[statName] = currentValue + increment;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets a statistic value.
    /// </summary>
    public int GetStatistic(string statName)
    {
        return Statistics.GetValueOrDefault(statName, 0);
    }

    /// <summary>
    /// Calculates level from experience points using a simple formula.
    /// </summary>
    private static int CalculateLevelFromExperience(int experience)
    {
        // Simple level calculation: level = floor(sqrt(experience / 100)) + 1
        return (int)Math.Floor(Math.Sqrt(experience / 100.0)) + 1;
    }

    /// <summary>
    /// Creates a deep copy of this progression data.
    /// </summary>
    public PlayerProgression Clone()
    {
        return new PlayerProgression(PlayerId)
        {
            Currency = Currency,
            TotalCurrencyEarned = TotalCurrencyEarned,
            Experience = Experience,
            Level = Level,
            UnlockedItems = new HashSet<string>(UnlockedItems),
            CompletedAchievements = new HashSet<string>(CompletedAchievements),
            Statistics = new Dictionary<string, int>(Statistics),
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            DataVersion = DataVersion,
            IsSyncedOnline = IsSyncedOnline
        };
    }

    public bool Equals(PlayerProgression other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return PlayerId == other.PlayerId;
    }

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj)) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != this.GetType()) return false;
        return Equals((PlayerProgression)obj);
    }

    public override int GetHashCode()
    {
        return PlayerId.GetHashCode();
    }
}
