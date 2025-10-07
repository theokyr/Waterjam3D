using Godot;

namespace Waterjam.Core.Services.Network;

/// <summary>
/// Null adapter for offline/local play without a network peer.
/// </summary>
public class NullNetworkAdapter : INetworkAdapter
{
    public NetworkBackend Backend => NetworkBackend.Null;

    public MultiplayerPeer Peer => null;

    public bool StartServer(int port, int maxPlayers)
    {
        return false;
    }

    public bool Connect(string address, int port)
    {
        return false;
    }

    public void Disconnect()
    {
    }
}


