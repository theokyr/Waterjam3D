using Godot;

namespace Waterjam.Core.Services.Network;

/// <summary>
/// ENet-based network adapter using Godot's ENetMultiplayerPeer.
/// </summary>
public class ENetNetworkAdapter : INetworkAdapter
{
    private ENetMultiplayerPeer _enetPeer;

    public NetworkBackend Backend => NetworkBackend.ENet;

    public MultiplayerPeer Peer => _enetPeer;

    public bool StartServer(int port, int maxPlayers)
    {
        _enetPeer = new ENetMultiplayerPeer();
        var err = _enetPeer.CreateServer(port, maxPlayers);
        if (err != Error.Ok)
        {
            _enetPeer = null;
            return false;
        }
        return true;
    }

    public bool Connect(string address, int port)
    {
        _enetPeer = new ENetMultiplayerPeer();
        var err = _enetPeer.CreateClient(address, port);
        if (err != Error.Ok)
        {
            _enetPeer = null;
            return false;
        }
        return true;
    }

    public void Disconnect()
    {
        if (_enetPeer != null)
        {
            _enetPeer.Close();
            _enetPeer = null;
        }
    }
}


