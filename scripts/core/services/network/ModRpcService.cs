using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Waterjam.Events;
using Waterjam.Core.Systems.Console;
using Waterjam.Core.Services.Modular;

namespace Waterjam.Core.Services.Network;

/// <summary>
/// Handles RPC communication between mods across the network.
/// Enforces permissions and rate limiting.
/// </summary>
public partial class ModRpcService : BaseService
{
    private readonly Dictionary<string, Dictionary<string, RpcHandler>> _rpcHandlers = new();
    private readonly Dictionary<string, RateLimiter> _rateLimiters = new();
    private readonly Dictionary<ulong, TaskCompletionSource<object>> _pendingCalls = new();

    private PermissionManager _permissions;
    private NetworkService _networkService;
    private ulong _nextCallId = 1;

    // Configuration
    private const int MAX_RPC_PER_SECOND = 100; // Per mod
    private const int MAX_RPC_ARGS = 16;

    public override void _Ready()
    {
        base._Ready();
        _permissions = new PermissionManager();
        _networkService = GetNodeOrNull<NetworkService>("/root/NetworkService");

        RegisterConsoleCommands();
        ConsoleSystem.Log("[ModRpcService] Initialized", ConsoleChannel.Network);
    }

    /// <summary>
    /// Register an RPC handler for a mod
    /// </summary>
    public void RegisterModRpc(
        string modId,
        string rpcName,
        RpcDirection direction,
        Func<object[], Task<object>> handler)
    {
        if (!_rpcHandlers.ContainsKey(modId))
        {
            _rpcHandlers[modId] = new Dictionary<string, RpcHandler>();
            _rateLimiters[modId] = new RateLimiter(MAX_RPC_PER_SECOND);
        }

        _rpcHandlers[modId][rpcName] = new RpcHandler
        {
            ModId = modId,
            Name = rpcName,
            Direction = direction,
            Handler = handler
        };

        ConsoleSystem.Log(
            $"[ModRpc] Registered {modId}.{rpcName} ({direction})",
            ConsoleChannel.Network
        );
    }

    /// <summary>
    /// Invoke an RPC (client or server)
    /// </summary>
    public async Task<object> InvokeRpc(string modId, string rpcName, params object[] args)
    {
        // Check permission
        if (!_permissions.CheckPermission(modId, ModPermission.SendRpc))
        {
            ConsoleSystem.LogErr(
                $"[ModRpc] Mod {modId} lacks SendRpc permission",
                ConsoleChannel.Network
            );
            return null;
        }

        // Check rate limit
        if (!_rateLimiters[modId].AllowRequest())
        {
            ConsoleSystem.LogWarn(
                $"[ModRpc] Mod {modId} rate limited",
                ConsoleChannel.Network
            );
            return null;
        }

        // Validate args
        if (args.Length > MAX_RPC_ARGS)
        {
            ConsoleSystem.LogErr(
                $"[ModRpc] Too many arguments ({args.Length} > {MAX_RPC_ARGS})",
                ConsoleChannel.Network
            );
            return null;
        }

        // Check if we should execute locally or remotely
        if (_networkService.IsServer)
        {
            // Server: execute locally
            return await ExecuteLocal(modId, rpcName, args);
        }
        else if (_networkService.IsClient)
        {
            // Client: send to server
            return await InvokeRemote(modId, rpcName, args);
        }

        return null;
    }

    private async Task<object> ExecuteLocal(string modId, string rpcName, object[] args)
    {
        if (!_rpcHandlers.TryGetValue(modId, out var modHandlers))
        {
            ConsoleSystem.LogErr($"[ModRpc] Mod {modId} has no registered RPCs", ConsoleChannel.Network);
            return null;
        }

        if (!modHandlers.TryGetValue(rpcName, out var handler))
        {
            ConsoleSystem.LogErr($"[ModRpc] RPC {rpcName} not found in mod {modId}", ConsoleChannel.Network);
            return null;
        }

        try
        {
            return await handler.Handler(args);
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr(
                $"[ModRpc] Error executing {modId}.{rpcName}: {ex.Message}",
                ConsoleChannel.Network
            );
            return null;
        }
    }

    private async Task<object> InvokeRemote(string modId, string rpcName, object[] args)
    {
        var callId = _nextCallId++;
        var tcs = new TaskCompletionSource<object>();
        _pendingCalls[callId] = tcs;

        try
        {
            // Serialize args
            var serializedArgs = SerializeArgs(args);

            // Send RPC call to server
            RpcId(1, nameof(ReceiveModRpcCallRpc), modId, rpcName, callId, serializedArgs);

            // Wait for response (with timeout)
            var timeoutTask = Task.Delay(5000); // 5 second timeout
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                ConsoleSystem.LogWarn($"[ModRpc] Timeout waiting for {modId}.{rpcName}", ConsoleChannel.Network);
                return null;
            }

            return await tcs.Task;
        }
        finally
        {
            _pendingCalls.Remove(callId);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private async void ReceiveModRpcCallRpc(string modId, string rpcName, ulong callId, byte[] serializedArgs)
    {
        try
        {
            var args = DeserializeArgs(serializedArgs);
            var result = await ExecuteLocal(modId, rpcName, args);

            // Send result back to caller
            var senderId = Multiplayer.GetRemoteSenderId();
            RpcId(senderId, nameof(ReceiveModRpcReplyRpc), callId, SerializeResult(result));
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[ModRpc] Error handling RPC: {ex.Message}", ConsoleChannel.Network);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer)]
    private void ReceiveModRpcReplyRpc(ulong callId, byte[] serializedResult)
    {
        if (_pendingCalls.TryGetValue(callId, out var tcs))
        {
            var result = DeserializeResult(serializedResult);
            tcs.SetResult(result);
        }
    }

    #region Serialization

    private byte[] SerializeArgs(object[] args)
    {
        // Simple binary serialization
        using var ms = new System.IO.MemoryStream();
        using var writer = new System.IO.BinaryWriter(ms);

        writer.Write(args.Length);

        foreach (var arg in args)
        {
            SerializeValue(writer, arg);
        }

        return ms.ToArray();
    }

    private object[] DeserializeArgs(byte[] data)
    {
        using var ms = new System.IO.MemoryStream(data);
        using var reader = new System.IO.BinaryReader(ms);

        var count = reader.ReadInt32();
        var args = new object[count];

        for (int i = 0; i < count; i++)
        {
            args[i] = DeserializeValue(reader);
        }

        return args;
    }

    private void SerializeValue(System.IO.BinaryWriter writer, object value)
    {
        switch (value)
        {
            case null:
                writer.Write((byte)0);
                break;
            case int intValue:
                writer.Write((byte)1);
                writer.Write(intValue);
                break;
            case float floatValue:
                writer.Write((byte)2);
                writer.Write(floatValue);
                break;
            case string stringValue:
                writer.Write((byte)3);
                writer.Write(stringValue);
                break;
            case bool boolValue:
                writer.Write((byte)4);
                writer.Write(boolValue);
                break;
            case Vector3 vec3:
                writer.Write((byte)5);
                writer.Write(vec3.X);
                writer.Write(vec3.Y);
                writer.Write(vec3.Z);
                break;
            default:
                writer.Write((byte)0); // Null for unsupported types
                break;
        }
    }

    private object DeserializeValue(System.IO.BinaryReader reader)
    {
        var typeId = reader.ReadByte();

        return typeId switch
        {
            0 => null,
            1 => reader.ReadInt32(),
            2 => reader.ReadSingle(),
            3 => reader.ReadString(),
            4 => reader.ReadBoolean(),
            5 => new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
            _ => null
        };
    }

    private byte[] SerializeResult(object result)
    {
        using var ms = new System.IO.MemoryStream();
        using var writer = new System.IO.BinaryWriter(ms);
        SerializeValue(writer, result);
        return ms.ToArray();
    }

    private object DeserializeResult(byte[] data)
    {
        using var ms = new System.IO.MemoryStream(data);
        using var reader = new System.IO.BinaryReader(ms);
        return DeserializeValue(reader);
    }

    #endregion

    // Violation tracking moved to PermissionManager

    private void RegisterConsoleCommands()
    {
        var consoleSystem = GetNodeOrNull<ConsoleSystem>("/root/ConsoleSystem");
        if (consoleSystem == null) return;

        consoleSystem.RegisterCommand(new ConsoleCommand(
            "mod_permissions",
            "Show permissions for a mod",
            "mod_permissions <mod_id>",
            async (args) =>
            {
                if (args.Length == 0)
                {
                    ConsoleSystem.Log("Usage: mod_permissions <mod_id>", ConsoleChannel.Network);
                    return false;
                }

                var modId = args[0];
                var perms = _permissions.GetModPermissions(modId);

                if (perms.Count == 0)
                {
                    ConsoleSystem.Log($"Mod {modId} has no permissions (or not registered)", ConsoleChannel.Network);
                }
                else
                {
                    ConsoleSystem.Log($"Permissions for {modId}:", ConsoleChannel.Network);
                    foreach (var perm in perms.OrderBy(p => p.ToString()))
                    {
                        ConsoleSystem.Log($"  - {perm}", ConsoleChannel.Network);
                    }
                }

                var disabled = _permissions.IsModDisabled(modId);
                ConsoleSystem.Log($"Disabled: {(disabled ? "YES" : "NO")}", ConsoleChannel.Network);

                return true;
            }));

        consoleSystem.RegisterCommand(new ConsoleCommand(
            "mod_rpc_stats",
            "Show RPC statistics",
            "mod_rpc_stats",
            async (args) =>
            {
                ConsoleSystem.Log("=== Mod RPC Statistics ===", ConsoleChannel.Network);
                ConsoleSystem.Log($"Registered mods: {_rpcHandlers.Count}", ConsoleChannel.Network);

                foreach (var (modId, handlers) in _rpcHandlers)
                {
                    ConsoleSystem.Log($"{modId}: {handlers.Count} RPCs", ConsoleChannel.Network);
                }

                var stats = _permissions.GetStats();
                ConsoleSystem.Log($"Total violations: {stats.TotalViolations}", ConsoleChannel.Network);
                ConsoleSystem.Log($"Disabled mods: {stats.DisabledMods}", ConsoleChannel.Network);

                return true;
            }));
    }

    public PermissionManager GetPermissionManager() => _permissions;
}

/// <summary>
/// RPC handler metadata
/// </summary>
public struct RpcHandler
{
    public string ModId;
    public string Name;
    public RpcDirection Direction;
    public Func<object[], Task<object>> Handler;
}

/// <summary>
/// RPC direction
/// </summary>
public enum RpcDirection
{
    ClientToServer,
    ServerToClient,
    Bidirectional
}

/// <summary>
/// Simple rate limiter
/// </summary>
public class RateLimiter
{
    private readonly Queue<ulong> _requests = new();
    private readonly int _maxPerSecond;

    public RateLimiter(int maxPerSecond)
    {
        _maxPerSecond = maxPerSecond;
    }

    public bool AllowRequest()
    {
        var now = Godot.Time.GetTicksMsec();
        var oneSecondAgo = now - 1000;

        // Remove old requests
        while (_requests.Count > 0 && _requests.Peek() < oneSecondAgo)
        {
            _requests.Dequeue();
        }

        // Check if under limit
        if (_requests.Count >= _maxPerSecond)
        {
            return false;
        }

        _requests.Enqueue(now);
        return true;
    }
}