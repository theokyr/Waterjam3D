using System.Collections.Generic;
using Waterjam.Core.Services;

namespace Waterjam.Events;

// Server events

public record NetworkServerStartedEvent(int Port) : IGameEvent;

public record NetworkLocalServerStartedEvent() : IGameEvent;

public record NetworkServerTickEvent(ulong Tick) : IGameEvent;

public record NetworkClientConnectedEvent(long PeerId) : IGameEvent;

public record NetworkClientDisconnectedEvent(long PeerId) : IGameEvent;

// Client events

public record NetworkConnectedToServerEvent(long ClientId) : IGameEvent;

public record NetworkConnectionFailedEvent() : IGameEvent;

public record NetworkDisconnectedFromServerEvent() : IGameEvent;

public record NetworkInputReceivedEvent(long ClientId, InputPacket Input) : IGameEvent;

// Replication events

public record NetworkEntitySpawnedEvent(ulong EntityId, string EntityType, Godot.Vector3 Position) : IGameEvent;

public record NetworkEntityDespawnedEvent(ulong EntityId) : IGameEvent;

public record NetworkEntityStateUpdateEvent(ulong EntityId, Dictionary<string, object> ComponentData) : IGameEvent;

// Mod sync events

public record NetworkModSyncRequestEvent(long ClientId, List<ModInfo> ClientMods) : IGameEvent;

public record NetworkModSyncResponseEvent(
    List<ModRequirement> RequiredMods,
    List<string> OptionalMods,
    List<string> ForbiddenPermissions
) : IGameEvent;

/// <summary>
/// Mod information from client
/// </summary>
public struct ModInfo
{
    public string Id;
    public string Version;
    public string Checksum;
}

/// <summary>
/// Server requirement for a mod
/// </summary>
public struct ModRequirement
{
    public string Id;
    public string Version;
    public string Checksum;
    public ModSide Side;

    // Pass 3: Repository support
    public string DownloadUrl;
    public ulong SizeBytes;
    public string RepositoryType; // "steam", "github", "modio", "http"
    public string RepositoryId; // Workshop ID, repo name, etc.
    public System.Collections.Generic.Dictionary<string, string> Metadata;

    public byte[] ToByteArray()
    {
        return System.Text.Encoding.UTF8.GetBytes(Checksum ?? string.Empty);
    }
}

/// <summary>
/// Where a mod runs
/// </summary>
public enum ModSide
{
    Client = 0,
    Server = 1,
    Both = 2
}