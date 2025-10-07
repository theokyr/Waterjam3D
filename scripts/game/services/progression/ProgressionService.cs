using System;
using System.IO;
using System.Text.Json;
using Godot;
using Waterjam.Core.Services;
using Waterjam.Core.Systems.Console;
using Waterjam.Domain.Progression;
using Waterjam.Events;
using Waterjam.Core.Services.Platform;

namespace Waterjam.Game.Services.Progression;

/// <summary>
/// Service for managing player progression data including currency, unlocks, and achievements.
/// Handles local persistence and online synchronization with services like Steam.
/// </summary>
public partial class ProgressionService : BaseService,
    IGameEventHandler<GameInitializedEvent>
{
    private const string ProgressionFilePath = "user://progression.json";
    private const string BackupFilePath = "user://progression_backup.json";

    private PlayerProgression _currentProgression;
    private bool _isDirty = false;
    private Timer _autoSaveTimer;
    private PlatformService _platformService;

    public override void _Ready()
    {
        base._Ready();
        ConsoleSystem.Log("ProgressionService initialized", ConsoleChannel.Game);

        // Set up auto-save timer (saves every 30 seconds if there are changes)
        _autoSaveTimer = new Timer();
        _autoSaveTimer.WaitTime = 30.0f;
        _autoSaveTimer.OneShot = false;
        _autoSaveTimer.Timeout += OnAutoSaveTimerTimeout;
        AddChild(_autoSaveTimer);

        // Register console commands for debugging
        RegisterConsoleCommands();

        // Resolve PlatformService for optional cloud/achievements integration
        _platformService = GetNodeOrNull("/root/PlatformService") as PlatformService;
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        // Auto-save on changes (immediate for important changes)
        if (_isDirty && _currentProgression != null)
        {
            _isDirty = false;
            SaveProgression();
        }
    }

    /// <summary>
    /// Gets the current player's progression data.
    /// </summary>
    public PlayerProgression GetCurrentProgression()
    {
        return _currentProgression?.Clone();
    }

    /// <summary>
    /// Sets the current player ID and loads their progression data.
    /// </summary>
    public void SetCurrentPlayer(string playerId)
    {
        if (string.IsNullOrEmpty(playerId))
        {
            ConsoleSystem.LogErr("Cannot set current player: playerId is null or empty", ConsoleChannel.Game);
            return;
        }

        // Save current progression if it exists
        if (_currentProgression != null)
        {
            SaveProgression();
        }

        // Load or create new progression for this player
        _currentProgression = LoadProgression(playerId) ?? CreateNewProgression(playerId);

        ConsoleSystem.Log($"Loaded progression for player: {playerId}", ConsoleChannel.Game);
        GameEvent.DispatchGlobal(new ProgressionLoadedEvent(_currentProgression.PlayerId));
    }

    /// <summary>
    /// Loads progression data for a specific player.
    /// </summary>
    private PlayerProgression LoadProgression(string playerId)
    {
        try
        {
            if (!File.Exists(ProjectSettings.GlobalizePath(ProgressionFilePath)))
            {
                ConsoleSystem.Log($"No progression file found at {ProgressionFilePath}", ConsoleChannel.Game);
                return null;
            }

            var json = File.ReadAllText(ProjectSettings.GlobalizePath(ProgressionFilePath));
            var progression = JsonSerializer.Deserialize<PlayerProgression>(json);

            if (progression == null)
            {
                ConsoleSystem.LogErr("Failed to deserialize progression data", ConsoleChannel.Game);
                return null;
            }

            // Verify this is the correct player's data
            if (progression.PlayerId != playerId)
            {
                ConsoleSystem.Log($"Progression file contains data for different player: {progression.PlayerId} vs {playerId}", ConsoleChannel.Game);
                return null;
            }

            ConsoleSystem.Log($"Loaded progression data for player {playerId}", ConsoleChannel.Game);
            return progression;
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"Failed to load progression data: {ex.Message}", ConsoleChannel.Game);

            // Try to load backup if main file is corrupted
            try
            {
                if (File.Exists(ProjectSettings.GlobalizePath(BackupFilePath)))
                {
                    var json = File.ReadAllText(ProjectSettings.GlobalizePath(BackupFilePath));
                    var progression = JsonSerializer.Deserialize<PlayerProgression>(json);

                    if (progression != null && progression.PlayerId == playerId)
                    {
                        ConsoleSystem.Log($"Loaded progression data from backup for player {playerId}", ConsoleChannel.Game);
                        return progression;
                    }
                }
            }
            catch (Exception backupEx)
            {
                ConsoleSystem.LogErr($"Failed to load backup progression data: {backupEx.Message}", ConsoleChannel.Game);
            }

            return null;
        }
    }

    /// <summary>
    /// Creates new progression data for a player.
    /// </summary>
    private PlayerProgression CreateNewProgression(string playerId)
    {
        var progression = new PlayerProgression(playerId);

        // Set up initial unlocks (everything unlocked for multiplayer as requested)
        progression.UnlockItem("basic_character");
        progression.UnlockItem("default_weapon");
        progression.UnlockItem("basic_ability");

        ConsoleSystem.Log($"Created new progression data for player {playerId}", ConsoleChannel.Game);
        return progression;
    }

    /// <summary>
    /// Saves the current progression data to disk.
    /// </summary>
    private void SaveProgression()
    {
        if (_currentProgression == null)
            return;

        try
        {
            var json = JsonSerializer.Serialize(_currentProgression, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            // Write to main file
            File.WriteAllText(ProjectSettings.GlobalizePath(ProgressionFilePath), json);

            // Create backup
            File.WriteAllText(ProjectSettings.GlobalizePath(BackupFilePath), json);

            ConsoleSystem.Log($"Saved progression data for player {_currentProgression.PlayerId}", ConsoleChannel.Game);

            // Attempt cloud sync via platform abstraction (non-blocking best-effort)
            TryCloudSync(json);
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"Failed to save progression data: {ex.Message}", ConsoleChannel.Game);
        }
    }

    /// <summary>
    /// Best-effort cloud sync via the current platform adapter (if available).
    /// </summary>
    private void TryCloudSync(string progressionJson)
    {
        if (_platformService?.Cloud?.IsAvailable != true)
            return;

        try
        {
            var data = System.Text.Encoding.UTF8.GetBytes(progressionJson);
            var ok = _platformService.Cloud.Save("progression.json", data);
            if (!ok)
            {
                ConsoleSystem.Log("Cloud sync skipped or failed (platform returned false)", ConsoleChannel.Game);
            }
        }
        catch (System.Exception ex)
        {
            ConsoleSystem.LogErr($"Cloud sync error: {ex.Message}", ConsoleChannel.Game);
        }
    }

    private void TryUnlockAchievementSafe(string id)
    {
        if (_platformService?.Achievements?.IsAvailable == true && !string.IsNullOrWhiteSpace(id))
        {
            try { _platformService.Achievements.Unlock(id); } catch { /* ignore */ }
        }
    }

    private void TrySetStatSafe(string name, int value)
    {
        if (_platformService?.Achievements?.IsAvailable == true && !string.IsNullOrWhiteSpace(name))
        {
            try { _platformService.Achievements.SetStat(name, value); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Manually saves the current progression data.
    /// </summary>
    public void ManualSave()
    {
        if (_currentProgression != null)
        {
            SaveProgression();
            ConsoleSystem.Log("Manual progression save completed", ConsoleChannel.Game);
        }
    }

    /// <summary>
    /// Adds currency to the current player's progression.
    /// </summary>
    public void AddCurrency(int amount)
    {
        if (_currentProgression == null)
        {
            ConsoleSystem.LogErr("Cannot add currency: no current progression loaded", ConsoleChannel.Game);
            return;
        }

        _currentProgression.AddCurrency(amount);
        _isDirty = true;

        GameEvent.DispatchGlobal(new CurrencyChangedEvent(_currentProgression.PlayerId, _currentProgression.Currency, amount));
    }

    /// <summary>
    /// Spends currency from the current player's progression.
    /// </summary>
    public bool SpendCurrency(int amount)
    {
        if (_currentProgression == null)
        {
            ConsoleSystem.LogErr("Cannot spend currency: no current progression loaded", ConsoleChannel.Game);
            return false;
        }

        var success = _currentProgression.SpendCurrency(amount);
        if (success)
        {
            _isDirty = true;
            GameEvent.DispatchGlobal(new CurrencyChangedEvent(_currentProgression.PlayerId, _currentProgression.Currency, -amount));
        }

        return success;
    }

    /// <summary>
    /// Unlocks an item for the current player.
    /// </summary>
    public void UnlockItem(string itemId)
    {
        if (_currentProgression == null)
        {
            ConsoleSystem.LogErr("Cannot unlock item: no current progression loaded", ConsoleChannel.Game);
            return;
        }

        _currentProgression.UnlockItem(itemId);
        _isDirty = true;

        GameEvent.DispatchGlobal(new ItemUnlockedEvent(_currentProgression.PlayerId, itemId));

        // Optional achievements mirror
        TryUnlockAchievementSafe(itemId);
    }

    /// <summary>
    /// Completes an achievement for the current player.
    /// </summary>
    public void CompleteAchievement(string achievementId)
    {
        if (_currentProgression == null)
        {
            ConsoleSystem.LogErr("Cannot complete achievement: no current progression loaded", ConsoleChannel.Game);
            return;
        }

        _currentProgression.CompleteAchievement(achievementId);
        _isDirty = true;

        GameEvent.DispatchGlobal(new AchievementCompletedEvent(_currentProgression.PlayerId, achievementId));

        // Mirror to platform achievements if available
        TryUnlockAchievementSafe(achievementId);
    }

    /// <summary>
    /// Updates a statistic for the current player.
    /// </summary>
    public void UpdateStatistic(string statName, int value)
    {
        if (_currentProgression == null)
        {
            ConsoleSystem.LogErr("Cannot update statistic: no current progression loaded", ConsoleChannel.Game);
            return;
        }

        _currentProgression.UpdateStatistic(statName, value);
        _isDirty = true;

        // Mirror to platform stats if available
        TrySetStatSafe(statName, value);
    }

    /// <summary>
    /// Increments a statistic for the current player.
    /// </summary>
    public void IncrementStatistic(string statName, int increment = 1)
    {
        if (_currentProgression == null)
        {
            ConsoleSystem.LogErr("Cannot increment statistic: no current progression loaded", ConsoleChannel.Game);
            return;
        }

        var oldValue = _currentProgression.GetStatistic(statName);
        _currentProgression.IncrementStatistic(statName, increment);
        _isDirty = true;

        GameEvent.DispatchGlobal(new StatisticChangedEvent(_currentProgression.PlayerId, statName, oldValue, oldValue + increment));

        // Mirror to platform stats if available
        TrySetStatSafe(statName, oldValue + increment);
    }

    public void OnGameEvent(GameInitializedEvent eventArgs)
    {
        // Initialize progression system when game starts
        _autoSaveTimer.Start();

        // For now, use a default player ID (in a real game, this would come from login/auth)
        var defaultPlayerId = "player_" + Guid.NewGuid().ToString().Substring(0, 8);
        SetCurrentPlayer(defaultPlayerId);
    }

    private void OnAutoSaveTimerTimeout()
    {
        if (_isDirty && _currentProgression != null)
        {
            SaveProgression();
        }
    }

    private void RegisterConsoleCommands()
    {
        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "progression_info",
            "Show current player progression info",
            "progression_info",
            async (args) =>
            {
                if (_currentProgression == null)
                {
                    ConsoleSystem.Log("No progression data loaded", ConsoleChannel.Game);
                    return true;
                }

                ConsoleSystem.Log($"Player: {_currentProgression.PlayerId}", ConsoleChannel.Game);
                ConsoleSystem.Log($"Currency: {_currentProgression.Currency}", ConsoleChannel.Game);
                ConsoleSystem.Log($"Total Earned: {_currentProgression.TotalCurrencyEarned}", ConsoleChannel.Game);
                ConsoleSystem.Log($"Level: {_currentProgression.Level}", ConsoleChannel.Game);
                ConsoleSystem.Log($"Experience: {_currentProgression.Experience}", ConsoleChannel.Game);
                ConsoleSystem.Log($"Unlocked Items: {string.Join(", ", _currentProgression.UnlockedItems)}", ConsoleChannel.Game);
                ConsoleSystem.Log($"Achievements: {_currentProgression.CompletedAchievements.Count}", ConsoleChannel.Game);
                ConsoleSystem.Log($"Statistics: {_currentProgression.Statistics.Count} tracked", ConsoleChannel.Game);

                return true;
            }));

        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "progression_add_currency",
            "Add currency to current player",
            "progression_add_currency <amount>",
            async (args) =>
            {
                if (args.Length == 0 || !int.TryParse(args[0], out var amount))
                {
                    ConsoleSystem.Log("Usage: progression_add_currency <amount>", ConsoleChannel.Game);
                    return false;
                }

                AddCurrency(amount);
                ConsoleSystem.Log($"Added {amount} currency", ConsoleChannel.Game);
                return true;
            }));

        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "progression_unlock_item",
            "Unlock an item for current player",
            "progression_unlock_item <itemId>",
            async (args) =>
            {
                if (args.Length == 0)
                {
                    ConsoleSystem.Log("Usage: progression_unlock_item <itemId>", ConsoleChannel.Game);
                    return false;
                }

                UnlockItem(args[0]);
                ConsoleSystem.Log($"Unlocked item: {args[0]}", ConsoleChannel.Game);
                return true;
            }));

        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "progression_save",
            "Manually save progression data",
            "progression_save",
            async (args) =>
            {
                ManualSave();
                ConsoleSystem.Log("Progression data saved", ConsoleChannel.Game);
                return true;
            }));
    }
}
