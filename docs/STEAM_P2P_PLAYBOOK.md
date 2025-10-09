# Game-Agnostic Playbook: Steam P2P with Godot Steam GDExtension

This guide provides a step-by-step approach to implementing Steam P2P networking in your Godot project using the Godot Steam and Steam Multiplayer Peer GDExtensions. It is based on the methods and advice presented in the tutorial by BatteryAcidDev.

This approach allows you to use Godot's high-level multiplayer APIs (MultiplayerSpawner, MultiplayerSynchronizer, RPCs) with Steam's robust backend, without needing a custom-compiled version of the engine.

Disclaimer: As noted in the video, this extension is not yet production-ready. It's excellent for prototypes and testing with friends, but it has known issues, including being broken on macOS and potentially causing log spam under certain conditions.

1. Initial Setup: Install the Extensions "Godot Steam" and "Steam Multiplayer Peer" (already done).

2. The Core Concept: Shifting from Peer to API
The fundamental difference between this GDExtension method and older, pre-compiled engine methods is how lobbies are managed.

Old Method: Lobby creation and joining were handled directly by functions within the SteamMultiplayerPeer object itself.

New GDExtension Method: The SteamMultiplayerPeer is now only responsible for creating the host and client connections. All lobby management (creating, listing, joining) is handled directly by the main Godot Steam API.

This separation is key to understanding the new workflow.

3. Implementation Steps
This section details the code implementation for hosting and joining a game.

3.1. Becoming a Host
Hosting is a two-step process: first create a Steam Lobby, then establish yourself as the multiplayer host within that lobby.

Step 1: Create the Steam Lobby
Connect to the lobby_created signal from the global Steam singleton. Then, call the function to create the lobby.

# In your network manager script
var multiplayer_peer = SteamMultiplayerPeer.new()

func become_host():
    # Connect to the signal that fires after a lobby is successfully created
    Steam.lobby_created.connect(_on_lobby_created)
    
    # Use the Steam API to create a public lobby
    # The 'LOBBY_NAME' constant should be unique to your game for filtering
    Steam.createLobby(Steam.LOBBY_TYPE_PUBLIC, 10) # Max 10 players


Step 2: Create the Multiplayer Host
Once the lobby_created signal fires, you can create the Godot MultiplayerPeer host.

func _on_lobby_created(result, lobby_id):
    if result == Steam.RESULT_OK:
        # The lobby was made, now create the peer-to-peer host
        var error = multiplayer_peer.create_host()
        if error == OK:
            # Set this peer as the active one for the whole game
            multiplayer.multiplayer_peer = multiplayer_peer
            
            # Now is a good time to add the host's player character to the scene
            add_player_to_game(Steam.getSteamID()) 
        else:
            print("Failed to create host, error code: ", error)
    else:
        print("Failed to create lobby, error code: ", result)

3.2. Listing and Joining a Lobby
Clients need to find and join the host's lobby before they can establish a P2P connection.

Step 1: Find Lobbies
To find lobbies, you must add a filter that matches the metadata of the host's lobby. Without a filter, Steam may not return your lobby from the vast pool of test lobbies using the default App ID (480).

const LOBBY_NAME = "my_unique_game_name_2233" # Should be the same on host and client

func list_lobbies():
    # IMPORTANT: Add a filter to find your specific game's lobbies
    Steam.addRequestLobbyListStringFilter("name", LOBBY_NAME) # "name" is an arbitrary key
    
    # You can add other filters, like for player count or distance
    Steam.requestLobbyList()

# You would then connect to Steam.lobby_list to get the results and display them in your UI.
# When a player clicks to join, call the join function with the lobby's ID.

Step 2: Join the Lobby
When the user selects a lobby, use the Steam API to join it.

func join_lobby(lobby_id):
    # Connect to the signal that fires after joining a lobby
    Steam.lobby_joined.connect(_on_lobby_joined)
    
    # Use the Steam API to join
    Steam.joinLobby(lobby_id)

3.3. Handling the Client Connection
This is the most crucial part for clients. The lobby_joined signal fires for everyone in the lobby, including the host who just created it. The client must use this callback to identify the host and create a P2P connection to them.

func _on_lobby_joined(result, lobby_id, permissions, locked, response):
    if result == Steam.RESULT_OK:
        # Get the Steam ID of the person who owns the lobby (the host)
        var host_id = Steam.getLobbyOwner(lobby_id)
        
        # Check if WE are the host. If so, we don't need to do anything else.
        if Steam.getSteamID() == host_id:
            return # We are the host, our setup is already done.
            
        # --- If we are here, we are a client that just joined ---
        # Now, create a client peer pointed at the host
        var error = multiplayer_peer.create_client(host_id)
        if error == OK:
            # Set this peer as the active one for the whole game
            multiplayer.multiplayer_peer = multiplayer_peer
        else:
            print("Failed to create client, error code: ", error)
    else:
        # The video provides a useful match statement to print detailed error reasons
        print("Failed to join lobby, response code: ", response)


4. Key Considerations & Best Practices
Signals are Key: The entire flow depends on properly connecting to and handling the lobby_created and lobby_joined signals from the Steam singleton.

Peer Connected/Disconnected: Remember to still connect to the standard multiplayer.peer_connected and multiplayer.peer_disconnected signals to handle players joining and leaving the game session itself.

Unique Lobby Name: As emphasized in the video, using a unique name for your lobby and filtering for it is essential for testing. When you create your lobby, set metadata (Steam.setLobbyData), and when you search, filter on that same metadata (Steam.addRequestLobbyListStringFilter).

Error Handling: The create_host() and create_client() functions return an error code. Always check this code to understand why a connection might have failed.

Lag and Prediction: This implementation only establishes the connection. For a smooth gameplay experience, you still need to implement standard network game strategies like client-side prediction and interpolation, which are outside the scope of this networking setup.