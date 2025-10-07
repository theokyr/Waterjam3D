using Godot;

namespace Waterjam.Core.Services.Network;

/// <summary>
/// Placeholder P2P adapter. Intended for custom peer-to-peer solutions.
/// Currently returns false for operations.
/// </summary>
public class P2PNetworkAdapter : INetworkAdapter
{
    public NetworkBackend Backend => NetworkBackend.P2P;

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


