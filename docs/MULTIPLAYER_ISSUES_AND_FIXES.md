# Multiplayer Issues & Fixes

## Current Problems (as of 2025-10-08 17:22)

### Issue 1: Multiple Lobbies Created
**Problem**: Each player creates their own separate Steam lobbies
- Host creates party lobby: `109775242412370602`
- Host creates ANOTHER game lobby when pressing "Multiplayer": `109775242412370773`
- Peer creates their own lobbies too: `109775242410036126` → `109775242410036849`

**Root Cause**: 
- `SteamNetworkAdapter.StartServer()` always calls `Steam.CreateLobby()`
- It doesn't check if a party lobby already exists
- Each "Multiplayer" press creates a NEW lobby instead of reusing the party lobby

**Fix**: Modify `MainMenu` to check for existing party lobby and have players join it instead of creating new ones

### Issue 2: Peers Don't Auto-Navigate to Lobby
**Problem**: When host presses "Multiplayer", peers stay on main menu
- Host sends: `[MainMenu] Sent lobby navigation message to all party members`
- But peers never receive/process it

**Root Cause**:
- `Steam.LobbyDataUpdate` callback commented out due to signature issues
- No polling mechanism to check for lobby data changes

**Fix**: Implement polling in `PartyService._Process()` to check lobby data

### Issue 3: Both Try to Become Leader
**Problem**: Both clients start their own game lobbies as leaders

**Root Cause**: When peer presses "Multiplayer", they call `networkService.StartServer(0)` which makes them a host too

**Fix**: Peers should JOIN the host's lobby, not create their own

## Implementation Plan

### Step 1: Modify MainMenu to prevent duplicate lobby creation
- Check if player is party leader
- If leader: reuse party lobby for game
- If not leader: wait for navigation message or manually join

### Step 2: Add lobby data polling in PartyService
- Check `navigate_to_lobby` flag every second
- Navigate non-leaders to lobby screen when flag is set

### Step 3: Fix lobby join flow
- Peers should connect as clients, not create new servers
- Use existing party Steam lobby ID for all networking

## Files to Modify
1. `scripts/ui/MainMenu.cs` - Check party leader status, prevent duplicate lobbies
2. `scripts/game/services/party/PartyService.cs` - Add polling for lobby data
3. `scripts/core/services/network/SteamNetworkAdapter.cs` - Support joining existing lobby as host
