# Party Bar Update Fix

## Issue
The party bar was not being properly updated when Steam P2P received lobby events (members joining/leaving).

## Root Cause
`SteamNetworkAdapter` was handling Steam lobby connections for networking but was not registering for the `LobbyChatUpdate` callback, which is the Steam event that fires when lobby members join or leave. This meant that when players joined or left via Steam P2P, the party system events (`PartyMemberJoinedEvent`, `PartyMemberLeftEvent`) were not being dispatched from the network layer, so the party bar UI wasn't being notified to refresh.

## Solution
Added `LobbyChatUpdate` callback handling to `SteamNetworkAdapter`:

1. **Register the callback** in the constructor (line 45):
   ```csharp
   Steam.LobbyChatUpdate += OnLobbyChatUpdate;
   ```

2. **Implemented `OnLobbyChatUpdate` handler** (lines 237-289):
   - Checks if the event is for the current lobby
   - Detects if it's a party lobby by checking for party metadata (`lobby_type` and `party_id`)
   - On member join: Creates a `PartyMember` and dispatches `PartyMemberJoinedEvent`
   - On member leave/disconnect/kick/ban: Dispatches `PartyMemberLeftEvent`

3. **Unregister the callback** in `Disconnect()` (line 416):
   ```csharp
   Steam.LobbyChatUpdate -= OnLobbyChatUpdate;
   ```

## How It Works
- `PartyBar` listens to `PartyMemberJoinedEvent` and `PartyMemberLeftEvent`
- When these events are dispatched, `PartyBar` calls `Refresh()` to update the UI
- Now both `PartyService` (for party lobbies) and `SteamNetworkAdapter` (for networking lobbies) can dispatch these events
- The adapter only dispatches party events if the lobby has party metadata, ensuring it doesn't interfere with pure game lobbies

## Coordination with PartyService
Both `PartyService` and `SteamNetworkAdapter` now listen to `LobbyChatUpdate`:
- `PartyService` maintains local party state and dispatches events
- `SteamNetworkAdapter` dispatches events when managing party lobbies for networking
- Both have guards against duplicate state changes
- If both dispatch events in the same frame, `PartyBar.Refresh()` uses `CallDeferred`, so it only refreshes once

## Files Modified
- `scripts/core/services/network/SteamNetworkAdapter.cs`

## Testing
Build successful with no new errors introduced.

