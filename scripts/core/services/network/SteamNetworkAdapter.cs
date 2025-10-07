using Godot;

namespace Waterjam.Core.Services.Network;

/// <summary>
/// Placeholder Steam adapter. Implement using GodotSteam or Facepunch.Steamworks as needed.
/// Currently returns false for operations.
/// </summary>
public class SteamNetworkAdapter : INetworkAdapter
{
    public NetworkBackend Backend => NetworkBackend.Steam;

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


