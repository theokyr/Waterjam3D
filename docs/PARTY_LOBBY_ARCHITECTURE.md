# Party & Lobby Architecture v2.0

## Overview
This document outlines the refactored Party and Lobby system, separating social grouping from game settings/matchmaking to eliminate Steam lobby conflation and simplify the multiplayer flow.

## Core Principles

### 1. Party = Social Layer
- **Purpose**: Group of friends who want to play together
- **Backed By**: Steam lobby (for invites and social presence only)
- **Lifetime**: Persists across multiple game sessions
- **Authority**: Party leader has control
- **Features**:
  - Friend invites via Steam overlay
  - Party chat
  - Member list with avatars
  - Leader promotion

### 2. Lobby = Game Configuration Layer
- **Purpose**: Game settings for the upcoming session (map, mode, difficulty)
- **Backed By**: Steam P2P messages (NOT Steam lobbies)
- **Lifetime**: Exists only during game setup phase
- **Authority**: Party leader controls settings
- **Features**:
  - Map selection
  - Game mode configuration
  - Difficulty settings
  - Networked via Steam P2P to all party members

### 3. Networking = Connection Layer
- **Purpose**: Real-time game communication
- **Backed By**: Steam P2P connections
- **Authority**: Party leader = network host
- **Entry Point**: Party's existing Steam lobby

## User Flow

### Solo Play
1. User clicks "Start Game" on main menu
2. Game loads immediately with default settings
3. No networking, no party

### Multiplayer Play
1. User is **always** in a party (self-party by default)
2. User invites friends via "+" button on party bar (Steam overlay)
3. Friends join the Steam lobby → automatically join party
4. User clicks "Start Game" on main menu
5. If party leader:
   - Lobby settings UI appears (map/mode selection)
   - Click "Start Game" → host network session → load level
   - Settings synced to all party members via Steam P2P
6. If party member:
   - Lobby settings UI appears (read-only)
   - Wait for leader to start
   - Receive settings sync → join network session → load level

## Technical Implementation

### PartyService
- Creates/manages Steam lobbies for social purposes
- Handles party member join/leave
- Manages party chat
- Does **NOT** handle game settings or networking

### LobbyService
- Manages game settings (LobbySettings domain object)
- Syncs settings via Steam P2P when leader changes them
- Does **NOT** create Steam lobbies
- Authority tied to party leader

### NetworkService
- Reuses party's Steam lobby for P2P connections
- Party leader = network host
- Other party members = network clients
- Handles all real-time game communication

### UI Components
- **PartyBar**: Always visible, shows party members + invite button
- **LobbySettingsPanel**: Appears next to PartyBar when starting game
  - Shows map, mode, difficulty
  - Editable for leader, read-only for members
  - Networked via Steam P2P
- **PartyChatPanel**: Collapsible panel for party chat

## Data Flow

### Party Creation
```
User → CreatePartyRequestEvent
  → PartyService creates Steam lobby
  → PartyCreatedEvent dispatched
  → UI updated
```

### Friend Joins Party
```
Steam lobby join (via overlay invite)
  → PartyService detects new member
  → PartyMemberJoinedEvent dispatched
  → UI updated
  → Party chat available
```

### Starting Game (Leader)
```
User clicks "Start Game"
  → MainMenu creates LobbySettings
  → LobbySettingsPanel appears
  → Leader configures map/mode/difficulty
  → Leader clicks "Start Game"
  → NetworkService starts host using party's Steam lobby
  → SceneService loads level
```

### Starting Game (Member)
```
Leader clicks "Start Game"
  → Member receives LobbySettings via Steam P2P
  → LobbySettingsPanel appears (read-only)
  → Leader starts game
  → Member receives game start command via Steam P2P
  → NetworkService connects to host via Steam P2P
  → SceneService loads level
```

## Migration from Old System

### Removed
- MainMenu multiplayer button removed
- LobbyUI standalone screen removed (replaced with panel)
- Complex lobby navigation messages removed

### Changed
- PartyService: Simplified to pure social layer
- MainMenu: Single "Start Game" button for all flows
- NetworkService: Reuses party Steam lobby instead of creating new one

### Added
- LobbySettingsPanel UI component
- PartyChatPanel UI component
- Lobby settings sync via Steam P2P
- Auto-party creation (user is always in a party)

## Benefits

1. **Simpler Mental Model**: Party = friends, Lobby = settings, Network = connections
2. **Fewer Steps**: One button to start game instead of multiple UI transitions
3. **No Conflicts**: Steam lobbies only used for social layer, not game networking
4. **Better UX**: Chat and settings always visible when relevant
5. **Clearer Authority**: Party leader = lobby settings authority = network host

## Edge Cases

### Leader Disconnects
- Party promotes new leader
- Lobby settings authority transfers
- Network host migration (TBD - for now, game ends)

### Mid-Game Party Join
- New member joins party but not active game session
- They see "Game in Progress" in lobby panel
- Can join next session

### Solo Player Joining Multiplayer
- Always in self-party by default
- Clicking "Start Game" shows lobby settings immediately
- Can invite friends at any time (even during game)

## Future Enhancements
- Lobby templates (save favorite settings)
- Party voice chat integration
- Advanced matchmaking (public lobbies)
- Host migration during gameplay

