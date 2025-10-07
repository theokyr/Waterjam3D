using System.Collections.Generic;
using Godot;
using GodotSteam;
using Waterjam.Core.Services;
using Waterjam.Core.Systems.Console;

namespace Waterjam.Core.Services.Platform;

/// <summary>
/// Steam-backed platform adapter using GodotSteam C# bindings for cloud storage, achievements, and friends.
/// </summary>
public class SteamPlatformAdapter : IPlatformAdapter
{
    private sealed class SteamCloudStorage : ICloudStorage
    {
        public bool IsAvailable => PlatformService.IsSteamInitialized && Steam.IsSteamRunning();

        public bool Save(string filename, byte[] data)
        {
            if (!IsAvailable || string.IsNullOrWhiteSpace(filename) || data == null)
                return false;

            try
            {
                return Steam.FileWrite(filename, data, data.Length);
            }
            catch (System.Exception ex)
            {
                ConsoleSystem.LogErr($"[SteamCloudStorage] Failed to save file {filename}: {ex.Message}", ConsoleChannel.System);
                return false;
            }
        }

        public byte[] Load(string filename)
        {
            if (!IsAvailable || string.IsNullOrWhiteSpace(filename))
                return null;

            try
            {
                var result = Steam.FileRead(filename, 0);
                if (result == null || !result.ContainsKey("data"))
                {
                    return null;
                }
                return result["data"].AsByteArray();
            }
            catch (System.Exception ex)
            {
                ConsoleSystem.LogErr($"[SteamCloudStorage] Failed to load file {filename}: {ex.Message}", ConsoleChannel.System);
                return null;
            }
        }
    }

    private sealed class SteamAchievements : IAchievementPlatform
    {
        public bool IsAvailable => PlatformService.IsSteamInitialized && Steam.IsSteamRunning();

        public bool Unlock(string achievementId)
        {
            if (!IsAvailable || string.IsNullOrWhiteSpace(achievementId))
                return false;

            try
            {
                var ok = Steam.SetAchievement(achievementId);
                if (ok)
                {
                    Steam.StoreStats();
                }
                return ok;
            }
            catch (System.Exception ex)
            {
                ConsoleSystem.LogErr($"[SteamAchievements] Failed to unlock achievement {achievementId}: {ex.Message}", ConsoleChannel.System);
                return false;
            }
        }

        public bool SetStat(string statName, int value)
        {
            if (!IsAvailable || string.IsNullOrWhiteSpace(statName))
                return false;

            try
            {
                var ok = Steam.SetStatInt(statName, value);
                if (ok)
                {
                    Steam.StoreStats();
                }
                return ok;
            }
            catch (System.Exception ex)
            {
                ConsoleSystem.LogErr($"[SteamAchievements] Failed to set stat {statName}: {ex.Message}", ConsoleChannel.System);
                return false;
            }
        }
    }

    public string PlatformName => "Steam";
    public ICloudStorage Cloud { get; } = new SteamCloudStorage();
    public IAchievementPlatform Achievements { get; } = new SteamAchievements();

    // Steam Authentication and Friends
    public ulong CurrentUserSteamId => PlatformService.IsSteamInitialized ? Steam.GetSteamID() : 0;
    public string CurrentUserDisplayName => PlatformService.IsSteamInitialized ? Steam.GetPersonaName() : "Unknown";
    public bool IsAuthenticated => PlatformService.IsSteamInitialized && Steam.GetSteamID() != 0;

    /// <summary>
    /// Gets a friend's display name by Steam ID.
    /// </summary>
    public string GetFriendDisplayName(ulong steamId)
    {
        if (!PlatformService.IsSteamInitialized) return "Unknown";
        
        try
        {
            return Steam.GetFriendPersonaName(steamId);
        }
        catch (System.Exception ex)
        {
            ConsoleSystem.LogErr($"[SteamPlatformAdapter] Failed to get friend name for {steamId}: {ex.Message}", ConsoleChannel.System);
            return "Unknown";
        }
    }

    /// <summary>
    /// Gets the current user's friends list.
    /// </summary>
    public List<SteamFriend> GetFriendsList()
    {
        var friends = new List<SteamFriend>();

        if (!PlatformService.IsSteamInitialized) return friends;

        try
        {
            int friendCount = Steam.GetFriendCount(FriendFlag.Immediate);
            for (int i = 0; i < friendCount; i++)
            {
                ulong friendSteamId = Steam.GetFriendByIndex(i, FriendFlag.Immediate);
                if (friendSteamId != 0)
                {
                    var friend = new SteamFriend
                    {
                        SteamId = friendSteamId,
                        DisplayName = Steam.GetFriendPersonaName(friendSteamId),
                        Status = Steam.GetFriendPersonaState(friendSteamId),
                        IsOnline = Steam.GetFriendPersonaState(friendSteamId) != PersonaState.Offline
                    };
                    friends.Add(friend);
                }
            }
        }
        catch (System.Exception ex)
        {
            ConsoleSystem.LogErr($"[SteamPlatformAdapter] Failed to get friends list: {ex.Message}", ConsoleChannel.System);
        }

        return friends;
    }

    /// <summary>
    /// Invites a friend to the current lobby.
    /// </summary>
    public bool InviteFriendToLobby(ulong friendSteamId)
    {
        if (!PlatformService.IsSteamInitialized) return false;

        try
        {
            // This would need to be implemented with proper Steam lobby integration
            // For now, return false as a placeholder
            ConsoleSystem.Log($"[SteamPlatformAdapter] Invite friend {friendSteamId} to lobby (not implemented)", ConsoleChannel.System);
            return false;
        }
        catch (System.Exception ex)
        {
            ConsoleSystem.LogErr($"[SteamPlatformAdapter] Failed to invite friend {friendSteamId}: {ex.Message}", ConsoleChannel.System);
            return false;
        }
    }
}

/// <summary>
/// Represents a Steam friend.
/// </summary>
public class SteamFriend
{
    public ulong SteamId { get; set; }
    public string DisplayName { get; set; }
    public PersonaState Status { get; set; }
    public bool IsOnline { get; set; }
}
