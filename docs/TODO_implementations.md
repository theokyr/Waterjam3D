# Network Implementation TODOs

This document outlines missing or stub implementations identified during the investigation of Steam P2P networking for player visibility.

## 1. NetworkService.cs

### Missing Implementations

*   **Input Processing (Client to Server)**: Clients are currently not sending their movement/action inputs to the server.
    *   **Location**: `NetworkService.cs`, `SendInput` method (line 565) - "TODO: Send to remote server"
*   **Server Input Processing**: The server is not processing client inputs received from remote clients.
    *   **Location**: `NetworkService.cs`, `ServerTick` method (line 282) - "TODO: Process input packets"
*   **World State Snapshots (Server to Clients)**: The server is not sending regular world state updates (e.g., player positions, rotations) to clients. This is a critical component for players to see each other's movements and interactions.
    *   **Location**: `NetworkService.cs`, `SendSnapshotsToClients` method (line 294) - "TODO: Gather world state and send to clients"

### Potential Issues / Areas for Re-evaluation

*   **Client Peer ID Discrepancy**: There's a potential mismatch in how client peer IDs are handled between the `SteamNetworkAdapter` (host assigns, client receives) and `SteamP2PMultiplayerPeer` (client implicitly sets its `_localPeerId` to 1). This needs to be resolved to ensure unique and correct peer IDs for clients.
*   **MultiplayerAuthority Setting**: The `MultiplayerAuthority` in `RpcClientSpawnPlayer` is set to the `peerId` of the player. While correct for the server creating its own instances, on clients, this means a remote client's player instance would have its authority set to the *remote client's* peer ID. This should likely be set to `1` (server) on clients, or a clear authority model defined and implemented across the board.

## 2. SteamNetworkAdapter.cs

### Potential Issues / Areas for Re-evaluation

*   **Client Peer ID Mapping and `_localPeerId` Synchronization**: The `SteamNetworkAdapter` correctly assigns a `peerId` to a client on the host side and sends it. However, the client's `SteamP2PMultiplayerPeer` needs to be explicitly updated with this assigned `peerId` for its `_localPeerId` to ensure proper client identification. Currently, the client maps the *host's* Steam ID to peer ID 1, but doesn't set its *own* unique `_localPeerId`.

## 3. SteamP2PMultiplayerPeer.cs

### Critical Issues

*   **Client `_localPeerId` Assignment**: The `_localPeerId` is initialized to `1` for both server and client. This is a fundamental flaw, as peer ID `1` is reserved for the server in Godot's multiplayer. Clients must have unique peer IDs greater than `1`. The `SetLocalPeerId` method exists but is not being effectively used by the `SteamNetworkAdapter` to communicate the client's assigned peer ID.
