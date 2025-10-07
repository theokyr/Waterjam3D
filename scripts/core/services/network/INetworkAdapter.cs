using Godot;
using Waterjam.Core.Systems.Console;

namespace Waterjam.Core.Services.Network;

/// <summary>
/// Pluggable networking adapter interface. Implementations create and manage a MultiplayerPeer
/// for different backends (ENet, Steam, custom P2P, etc.).
/// </summary>
public interface INetworkAdapter
{
    NetworkBackend Backend { get; }

    /// <summary>
    /// Returns the active MultiplayerPeer instance created by this adapter, or null if not connected.
    /// </summary>
    MultiplayerPeer Peer { get; }

    /// <summary>
    /// Start a dedicated server on the specified port.
    /// </summary>
    bool StartServer(int port, int maxPlayers);

    /// <summary>
    /// Connect to a remote server.
    /// </summary>
    bool Connect(string address, int port);

    /// <summary>
    /// Disconnect and release any resources.
    /// </summary>
    void Disconnect();
}


