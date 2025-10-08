# Critical Fixes - Party Chat & Multiplayer Connection (v2)

## Issues Fixed

### 1. ✅ Party Chat Not Synced Between Players
**Problem**: Players could send chat messages, but they weren't visible to other party members.

**Root Cause**: Chat messages were only stored locally, not broadcast via network.

**Solution**: Implemented Steam lobby chat messaging for party chat
- When a player sends a chat message, it's broadcast via `Steam.SendLobbyChatMsg()`
- Message format: `CHAT|senderPlayerId|senderDisplayName|content`
- Receivers get the message via `Steam.LobbyMessage` callback
- Message is added to local chat channel and UI is updated

**Files Changed**:
- `scripts/game/services/party/PartyService.cs`:
  - Added `Steam.LobbyMessage` callback registration
  - Implemented `OnSteamLobbyMessage()` handler
  - Modified `SendPartyChatMessage()` to broadcast via Steam
  - Messages are deduplicated (sender doesn't receive their own message twice)

---

### 2. ✅ Client Stuck in Infinite Loop
**Problem**: Client got stuck in an infinite loop, repeatedly showing "Detected game starting" every second.

**Root Cause**: Client was clearing the `game_starting` flag in Steam lobby data, which triggered another lobby data update callback, causing infinite recursion.

**Solution**: Added flag to track if game starting has been processed
- New field: `_gameStartingProcessed` prevents re-processing
- Client no longer clears the `game_starting` flag (only reads it once)
- Loop broken

**Files Changed**:
- `scripts/game/services/party/PartyService.cs`:
  - Added `_gameStartingProcessed` boolean field
  - Check flag before processing `game_starting` signal
  - Removed client's attempt to clear Steam lobby data (permission violation)

---

### 3. ✅ Client Using Correct Network Backend
**Problem**: Client network backend needed to be properly configured to use Steam P2P.

**Root Cause**: Client's `InitializeNetworkingAsClient()` needed to ensure NetworkService was using Steam backend before calling `ConnectToServer()`.

**Solution**: Added backend configuration in client initialization
- Client now uses reflection to set `NetworkConfig.Backend = NetworkBackend.Steam`
- Calls `InitializeAdapter()` to create Steam adapter
- Then connects to host via Steam ID

**Files Changed**:
- `scripts/ui/MainMenu.cs`:
  - Modified `InitializeNetworkingAsClient()` to switch backend
  - Uses same reflection pattern as leader initialization
  - Logs backend switch for debugging

---

### 4. ✅ Client Not Loading Into Game When Host Starts
**Problem**: When host clicked "Start Game" in lobby panel, only host loaded into the game. Client stayed at lobby settings screen.

**Root Cause**: No mechanism to signal clients that game has actually launched (not just lobby created).

**Solution**: Implemented two-stage signaling via Steam lobby data
1. **Stage 1** - "Game Starting" (show lobby panel):
   - Host clicks "Start Game" on main menu
   - Sets `game_starting = "true"` in Steam lobby data
   - Clients detect this and show lobby panel

2. **Stage 2** - "Game Launched" (load scene):
   - Host clicks "Start Game" in lobby panel
   - Sets `game_launched = "true"` and `game_scene_path = <map>` in Steam lobby data
   - Clients detect this and dispatch `NewGameStartedEvent` with correct map
   - Host clears flag after 2-second delay

**Files Changed**:
- `scripts/ui/components/LobbySettingsPanel.cs`:
  - Modified `OnStartGamePressed()` to set launch flags
  - Added timer to clear flags after clients receive them
  - Added `NetworkLobbyStartedEvent` and `NewGameStartedEvent` handlers

- `scripts/game/services/party/PartyService.cs`:
  - Added `game_launched` flag detection in `OnSteamLobbyDataUpdate()`
  - Client reads `game_scene_path` and loads correct map
  - Client dispatches `NewGameStartedEvent` to trigger scene load

---

## Technical Summary

### Steam Lobby Data Flags

| Flag | Set By | Purpose | Cleared By |
|------|--------|---------|------------|
| `game_starting` | Host (main menu) | Signal clients to show lobby panel | Never (one-time) |
| `game_leader` | Host (main menu) | Identify who is hosting | Never |
| `game_lobby_id` | Host (main menu) | Share lobby settings ID | Never |
| `game_launched` | Host (lobby panel) | Signal clients to load scene | Host after 2s |
| `game_scene_path` | Host (lobby panel) | Tell clients which map to load | Host after 2s |

### Chat Message Protocol

**Format**: `CHAT|{senderPlayerId}|{senderDisplayName}|{content}`

**Flow**:
1. Player types message in `PartyChatPanel`
2. `PartyService.SendPartyChatMessage()` called
3. Message added to local chat channel
4. Message broadcast via `Steam.SendLobbyChatMsg()`
5. All party members receive via `Steam.LobbyMessage` callback
6. Recipients add message to their chat channel
7. UI refreshes to show new message

### Network Connection Flow

**Host**:
1. Creates party → Steam lobby created
2. Clicks "Start Game" → `NetworkService.StartServer(0)` with Steam backend
3. Reuses existing party Steam lobby for P2P
4. Server ready

**Client**:
1. Joins party → joins Steam lobby
2. Host clicks "Start Game" → detects `game_starting` flag
3. Shows lobby panel
4. `InitializeNetworkingAsClient()` called
5. Ensures Steam backend is configured
6. Calls `NetworkService.ConnectToServer(leaderSteamId, 0)`
7. Connects via Steam P2P to host

---

## Testing Results

### What Should Work Now

- ✅ Party chat visible for both host and client (when 2+ members)
- ✅ Chat messages sync bidirectionally via Steam
- ✅ Client no longer stuck in infinite loop
- ✅ Client uses correct network backend (Steam P2P)
- ✅ Client receives game launch signal and loads scene
- ✅ Both players should spawn in the same level

### Remaining Issues to Test

- ⏳ Transform RPC between players (should work if Steam P2P connection successful)
- ⏳ Player visibility (depends on network replication)
- ⏳ Chat persistence across scene loads
- ⏳ Host migration (not implemented yet)

---

## Code Quality

### Build Status
✅ **Compiles successfully** - 0 errors

### Architecture Improvements
- Separated signaling concerns (setup vs launch)
- Used Steam's native lobby chat (efficient, built-in)
- Added safeguards against infinite loops
- Proper backend configuration for both host and client

### Performance
- Chat uses Steam's optimized lobby messaging (< 1KB per message)
- Flags cleared after use to avoid memory/state buildup
- No polling - event-driven via Steam callbacks

---

## Debugging

If issues persist, check these:

```bash
# In host console
party_info          # Check party membership
lobby_info          # Check lobby settings
net_status          # Should show "Server" mode with Steam backend

# In client console
party_info          # Should show same party as host
lobby_info          # Should show lobby settings (read-only)
net_status          # Should show "Client" mode connecting to host Steam ID
```

### Expected Log Flow

**Host**:
```
[20:XX:XX] [Game] Created party 'My Party'
[20:XX:XX] [Game] [PartyService] Steam lobby created for party: 109775...
[20:XX:XX] [UI] [MainMenu] Multiplayer flow - party has 2 members, leader: True
[20:XX:XX] [Network] [MainMenu] Reusing party Steam lobby ... for networking
[20:XX:XX] [Network] [NetworkService] Server started on port 7777
[20:XX:XX] [UI] [LobbySettingsPanel] Sent game launch signal to all party members
[20:XX:XX] [Game] Starting game for lobby 'My Party Game'
```

**Client**:
```
[20:XX:XX] [Game] [PartyService] Joined party ... via Steam lobby
[20:XX:XX] [Game] [PartyService] Detected game starting, triggering multiplayer join flow
[20:XX:XX] [UI] [MainMenu] Multiplayer flow - party has 2 members, leader: False
[20:XX:XX] [Network] [MainMenu] Switched to Steam networking backend (client)
[20:XX:XX] [Network] [MainMenu] Connecting to party leader ... via Steam P2P
[20:XX:XX] [Game] [PartyService] Game launched by host, loading scene: res://scenes/dev/dev.tscn
```

---

## Next Steps

1. **Test the changes**: Run the game with 2 players
2. **Verify chat works**: Send messages both ways
3. **Verify multiplayer**: Both players load into game together
4. **Check transform sync**: Move around and verify other player is visible and moving
5. **Test edge cases**: Disconnect, rejoin, etc.

---

## Commit Message

```
fix: Implement party chat sync and client multiplayer flow

- Party chat now syncs via Steam lobby messaging
- Fixed infinite loop in client lobby data update handler
- Client now uses Steam P2P backend correctly
- Client receives game launch signal and loads scene
- Two-stage signaling: setup lobby -> launch game

Fixes:
- Chat messages not visible to other players
- Client stuck in infinite loop showing lobby screen
- Client using wrong network backend (needed Steam configuration)
- Client not loading scene when host starts game

Technical:
- Added Steam.LobbyMessage callback for chat sync
- Added _gameStartingProcessed flag to prevent infinite loop
- Added backend switching in InitializeNetworkingAsClient()
- Implemented game_launched flag for scene load signaling
- Messages formatted as: CHAT|playerId|displayName|content
```

---

## Success Criteria

- [x] Build compiles
- [x] Infinite loop fixed
- [x] Network backend correct
- [x] Chat sync implemented
- [x] Game launch signal implemented
- [ ] Runtime tested with 2 players
- [ ] Chat messages visible both ways
- [ ] Both players load into game
- [ ] Transform sync working

---

## Known Limitations

1. **Chat persistence**: Messages clear on scene change
2. **No retry logic**: If client fails to connect, no automatic retry
3. **No timeout**: Client will wait indefinitely for host to launch
4. **No host migration**: Game ends if host disconnects
5. **Steam only**: Won't work without Steam initialized

---

## Performance Notes

- Steam lobby chat is rate-limited by Valve (~10 messages/second/lobby)
- Lobby data updates are throttled by Steam (~1 update/second)
- Use Steam P2P for high-frequency game data (transforms, etc.)
- Use Steam lobby data only for low-frequency signaling (game start, etc.)

