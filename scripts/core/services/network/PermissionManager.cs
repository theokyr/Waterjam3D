using System;
using System.Collections.Generic;
using System.Linq;
using Waterjam.Core.Systems.Console;
using Waterjam.Core.Services.Modular;

namespace Waterjam.Core.Services.Network;

/// <summary>
/// Permission types that mods can request
/// </summary>
public enum ModPermission
{
    // World access
    ReadWorldChunks,
    WriteWorldChunks,
    SpawnEntities,
    DespawnEntities,
    ModifyTerrain,

    // Network
    SendRpc,
    ReceiveRpc,
    ReplicateComponents,
    ModifyNetworkState,

    // UI
    CreateUiOverlay,
    ModifyHud,
    ShowDialogs,
    CreateWindows,

    // System
    ReadFiles,
    WriteFiles,
    NetworkAccess, // External HTTP
    ExecuteNative,
    ModifySettings,

    // Dangerous
    ModifyGameState,
    AccessPlayerData,
    ModifyOtherMods,
    BypassSecurity
}

/// <summary>
/// Manages and enforces permissions for mods.
/// Prevents unauthorized actions and ensures server security.
/// </summary>
public class PermissionManager
{
    private readonly Dictionary<string, HashSet<ModPermission>> _modPermissions = new();
    private readonly HashSet<ModPermission> _forbiddenPermissions = new();
    private readonly Dictionary<string, int> _violationCounts = new();

    private const int MAX_VIOLATIONS = 5; // Disable mod after 5 violations

    /// <summary>
    /// Register a mod's requested permissions
    /// </summary>
    public void RegisterMod(string modId, string[] requestedPermissions)
    {
        var permissions = new HashSet<ModPermission>();

        foreach (var permString in requestedPermissions)
        {
            if (Enum.TryParse<ModPermission>(permString, out var permission))
            {
                // Check if permission is forbidden
                if (_forbiddenPermissions.Contains(permission))
                {
                    ConsoleSystem.LogWarn(
                        $"[Permissions] Mod {modId} requested forbidden permission {permission}",
                        ConsoleChannel.Network
                    );
                    continue;
                }

                permissions.Add(permission);
            }
            else
            {
                ConsoleSystem.LogWarn(
                    $"[Permissions] Mod {modId} requested unknown permission: {permString}",
                    ConsoleChannel.Network
                );
            }
        }

        _modPermissions[modId] = permissions;
        _violationCounts[modId] = 0;

        ConsoleSystem.Log(
            $"[Permissions] Registered {modId} with {permissions.Count} permissions",
            ConsoleChannel.Network
        );
    }

    /// <summary>
    /// Check if a mod has a specific permission
    /// </summary>
    public bool CheckPermission(string modId, ModPermission permission)
    {
        // Check if permission is globally forbidden
        if (_forbiddenPermissions.Contains(permission))
        {
            RecordViolation(modId, permission, "Forbidden permission");
            return false;
        }

        // Check if mod is registered
        if (!_modPermissions.TryGetValue(modId, out var permissions))
        {
            RecordViolation(modId, permission, "Unregistered mod");
            return false;
        }

        // Check if mod has this permission
        if (!permissions.Contains(permission))
        {
            RecordViolation(modId, permission, "Permission not granted");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Check multiple permissions at once
    /// </summary>
    public bool CheckPermissions(string modId, params ModPermission[] requiredPermissions)
    {
        foreach (var permission in requiredPermissions)
        {
            if (!CheckPermission(modId, permission))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Set globally forbidden permissions (server policy)
    /// </summary>
    public void SetForbiddenPermissions(IEnumerable<ModPermission> permissions)
    {
        _forbiddenPermissions.Clear();
        foreach (var perm in permissions)
        {
            _forbiddenPermissions.Add(perm);
        }

        ConsoleSystem.Log(
            $"[Permissions] Set {_forbiddenPermissions.Count} forbidden permissions",
            ConsoleChannel.Network
        );
    }

    /// <summary>
    /// Get all permissions for a mod
    /// </summary>
    public HashSet<ModPermission> GetModPermissions(string modId)
    {
        return _modPermissions.GetValueOrDefault(modId, new HashSet<ModPermission>());
    }

    /// <summary>
    /// Check if mod has been disabled due to violations
    /// </summary>
    public bool IsModDisabled(string modId)
    {
        return _violationCounts.GetValueOrDefault(modId, 0) >= MAX_VIOLATIONS;
    }

    private void RecordViolation(string modId, ModPermission permission, string reason)
    {
        if (!_violationCounts.ContainsKey(modId))
        {
            _violationCounts[modId] = 0;
        }

        _violationCounts[modId]++;

        ConsoleSystem.LogWarn(
            $"[Permissions] Violation #{_violationCounts[modId]} for {modId}: " +
            $"attempted {permission} - {reason}",
            ConsoleChannel.Network
        );

        if (_violationCounts[modId] >= MAX_VIOLATIONS)
        {
            ConsoleSystem.LogErr(
                $"[Permissions] Mod {modId} disabled after {MAX_VIOLATIONS} violations",
                ConsoleChannel.Network
            );

            // Dispatch event to disable mod
            // GameEvent.DispatchGlobal(new ModDisabledEvent(modId, "Security violations"));
        }
    }

    /// <summary>
    /// Reset violation count (e.g., after mod reload)
    /// </summary>
    public void ResetViolations(string modId)
    {
        _violationCounts[modId] = 0;
    }

    /// <summary>
    /// Get statistics for monitoring
    /// </summary>
    public PermissionStats GetStats()
    {
        return new PermissionStats
        {
            TotalModsRegistered = _modPermissions.Count,
            TotalViolations = _violationCounts.Values.Sum(),
            DisabledMods = _violationCounts.Count(kvp => kvp.Value >= MAX_VIOLATIONS),
            ForbiddenPermissionsCount = _forbiddenPermissions.Count
        };
    }
}

/// <summary>
/// Permission statistics
/// </summary>
public struct PermissionStats
{
    public int TotalModsRegistered;
    public int TotalViolations;
    public int DisabledMods;
    public int ForbiddenPermissionsCount;
}