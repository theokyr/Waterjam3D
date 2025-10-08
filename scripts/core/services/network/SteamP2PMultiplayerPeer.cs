using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodotSteam;

namespace Waterjam.Core.Services.Network;

/// <summary>
/// Steam-backed MultiplayerPeer using GodotSteam P2P (channel 0, reliable).
/// This enables Godot RPC to operate over Steam networking.
/// </summary>
public partial class SteamP2PMultiplayerPeer : MultiplayerPeerExtension
{
    private readonly Dictionary<int, ulong> _peerIdToSteamId = new();
    private readonly Dictionary<ulong, int> _steamIdToPeerId = new();

    private readonly Queue<byte[]> _incomingPackets = new();
    private readonly Queue<int> _incomingPeers = new();

    private int _targetPeer = 0; // 0 = broadcast
    private int _localPeerId = 1; // server = 1
    private bool _isServer;
    private MultiplayerPeer.ConnectionStatus _status = MultiplayerPeer.ConnectionStatus.Connected;
    private MultiplayerPeer.TransferModeEnum _transferMode = MultiplayerPeer.TransferModeEnum.Reliable;
    private int _transferChannel = 0;

    public void InitializeAsHost(ulong localSteamId)
    {
        _isServer = true;
        _localPeerId = 1;
        MapPeer(1, localSteamId);
        _status = MultiplayerPeer.ConnectionStatus.Connected;
    }

    public void InitializeAsClient(ulong hostSteamId)
    {
        _isServer = false;
        MapPeer(1, hostSteamId); // Host always peer 1
        _status = MultiplayerPeer.ConnectionStatus.Connected;
    }

    public void SetLocalPeerId(int peerId)
    {
        _localPeerId = peerId;
    }

    public void MapPeer(int peerId, ulong steamId)
    {
        _peerIdToSteamId[peerId] = steamId;
        _steamIdToPeerId[steamId] = peerId;
    }

    public void UnmapPeer(ulong steamId)
    {
        if (_steamIdToPeerId.TryGetValue(steamId, out var pid))
        {
            _steamIdToPeerId.Remove(steamId);
            _peerIdToSteamId.Remove(pid);
        }
    }

    public void EnqueueIncoming(byte[] data, ulong steamId)
    {
        var fromPeer = _steamIdToPeerId.GetValueOrDefault(steamId, 0);
        _incomingPackets.Enqueue(data);
        _incomingPeers.Enqueue(fromPeer);
    }

    public override void _Poll()
    {
        // Steam.RunCallbacks is called elsewhere; we only consume enqueued packets here
    }

    public override void _Close()
    {
        _incomingPackets.Clear();
        _incomingPeers.Clear();
        _peerIdToSteamId.Clear();
        _steamIdToPeerId.Clear();
        _status = MultiplayerPeer.ConnectionStatus.Disconnected;
    }

    public override int _GetAvailablePacketCount()
    {
        return _incomingPackets.Count;
    }

    public override int _GetMaxPacketSize()
    {
        // Steam networking typical safe MTU for reliable packets over internet is ~1200 bytes.
        // Godot RPC will chunk larger payloads if needed. Return a conservative value.
        return 1200;
    }

    public override void _DisconnectPeer(int peerId, bool force)
    {
        if (_peerIdToSteamId.TryGetValue(peerId, out var steamId))
        {
            try { Steam.CloseP2PSessionWithUser(steamId); } catch { }
            _peerIdToSteamId.Remove(peerId);
            _steamIdToPeerId.Remove(steamId);
        }
    }

    public override int _GetUniqueId()
    {
        return _localPeerId;
    }

    public override bool _IsServer()
    {
        return _isServer;
    }

    public override int _GetPacketChannel()
    {
        return _transferChannel;
    }

    public override int _GetPacketPeer()
    {
        if (_incomingPeers.Count == 0) return 0;
        return _incomingPeers.Peek();
    }


    public override MultiplayerPeer.ConnectionStatus _GetConnectionStatus()
    {
        return _status;
    }

    public override MultiplayerPeer.TransferModeEnum _GetTransferMode()
    {
        return _transferMode;
    }

    public override void _SetTransferChannel(int channel)
    {
        _transferChannel = channel;
    }

    public override void _SetTransferMode(MultiplayerPeer.TransferModeEnum mode)
    {
        _transferMode = mode;
    }

    public override void _SetTargetPeer(int peerId)
    {
        _targetPeer = peerId;
    }
}


