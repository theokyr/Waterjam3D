using Godot;
using System;
using System.Linq;
using Waterjam.Domain.Lobby;
using Waterjam.Events;

namespace Waterjam.UI;

/// <summary>
/// UI for managing game lobbies.
/// </summary>
public partial class LobbyUI : Control,
    IGameEventHandler<LobbyCreatedEvent>,
    IGameEventHandler<LobbyJoinedEvent>,
    IGameEventHandler<LobbyLeftEvent>,
    IGameEventHandler<LobbyPlayerJoinedEvent>,
    IGameEventHandler<LobbyPlayerLeftEvent>,
    IGameEventHandler<LobbyLeaderChangedEvent>,
    IGameEventHandler<LobbySettingsChangedEvent>,
    IGameEventHandler<LobbyStartedEvent>,
    IGameEventHandler<LobbyEndedEvent>,
    IGameEventHandler<LobbyPlayerReadyChangedEvent>
{
    [Signal]
    public delegate void BackPressedEventHandler();
    private Label _lobbyNameLabel;
    private Label _playersCountLabel;
    private Label _statusLabel;
    private ItemList _playersList;
    private Button _readyButton;
    private Button _leaveLobbyButton;
    private Label _leaderTitle;
    private Button _changeLeaderButton;
    private Button _startGameButton;
    private Label _mapLabel;
    private Label _gameModeLabel;
    private Label _difficultyLabel;

    private string _localPlayerId;

    public override void _Ready()
    {
        base._Ready();

        // Get UI elements
        _lobbyNameLabel = GetNode<Label>("Panel/VBoxContainer/HBoxContainer/LeftPanel/LobbyInfo/LobbyName");
        _playersCountLabel = GetNode<Label>("Panel/VBoxContainer/HBoxContainer/LeftPanel/LobbyInfo/PlayersCount");
        _statusLabel = GetNode<Label>("Panel/VBoxContainer/HBoxContainer/LeftPanel/LobbyInfo/Status");
        _playersList = GetNode<ItemList>("Panel/VBoxContainer/HBoxContainer/LeftPanel/PlayersList");
        _readyButton = GetNode<Button>("Panel/VBoxContainer/HBoxContainer/LeftPanel/PlayerActions/ReadyButton");
        _leaveLobbyButton = GetNode<Button>("Panel/VBoxContainer/HBoxContainer/LeftPanel/PlayerActions/LeaveLobbyButton");
        _leaderTitle = GetNode<Label>("Panel/VBoxContainer/HBoxContainer/RightPanel/LeaderActions/LeaderTitle");
        _changeLeaderButton = GetNode<Button>("Panel/VBoxContainer/HBoxContainer/RightPanel/LeaderActions/ChangeLeaderButton");
        _startGameButton = GetNode<Button>("Panel/VBoxContainer/HBoxContainer/RightPanel/LeaderActions/StartGameButton");
        _mapLabel = GetNode<Label>("Panel/VBoxContainer/HBoxContainer/RightPanel/Settings/MapLabel");
        _gameModeLabel = GetNode<Label>("Panel/VBoxContainer/HBoxContainer/RightPanel/Settings/GameModeLabel");
        _difficultyLabel = GetNode<Label>("Panel/VBoxContainer/HBoxContainer/RightPanel/Settings/DifficultyLabel");

        // Get local player ID from party service (assuming it's set)
        var partyService = GetNode("/root/PartyService") as Waterjam.Game.Services.Party.PartyService;
        if (partyService != null)
        {
            _localPlayerId = partyService.GetLocalPlayerId();
        }

        UpdateLobbyDisplay();
    }

    private void UpdateLobbyDisplay()
    {
        var lobbyService = GetNode("/root/LobbyService") as Waterjam.Game.Services.Lobby.LobbyService;
        if (lobbyService == null)
        {
            GD.PushWarning("LobbyService not found");
            return;
        }

        var currentLobby = lobbyService.GetCurrentPlayerLobby();

        if (currentLobby == null)
        {
            // Not in a lobby, show message or hide UI
            _lobbyNameLabel.Text = "Lobby: Not in lobby";
            _playersCountLabel.Text = "Players: 0/8";
            _statusLabel.Text = "Status: N/A";
            _playersList.Clear();
            _readyButton.Visible = false;
            _leaveLobbyButton.Visible = false;
            _leaderTitle.Visible = false;
            _changeLeaderButton.Visible = false;
            _startGameButton.Visible = false;
        }
        else
        {
            var isLeader = currentLobby.LeaderPlayerId == _localPlayerId;
            var localPlayer = currentLobby.GetPlayer(_localPlayerId);

            _lobbyNameLabel.Text = $"Lobby: {currentLobby.DisplayName}";
            _playersCountLabel.Text = $"Players: {currentLobby.Players.Count}/{currentLobby.MaxPlayers}";
            _statusLabel.Text = $"Status: {currentLobby.Status}";
            _readyButton.Visible = true;
            _leaveLobbyButton.Visible = true;
            _leaderTitle.Visible = isLeader;
            _changeLeaderButton.Visible = isLeader;
            _startGameButton.Visible = isLeader;

            // Update ready button text
            if (localPlayer != null)
            {
                _readyButton.Text = localPlayer.IsReady ? "Not Ready" : "Ready";
            }

            // Update settings
            _mapLabel.Text = $"Map: {currentLobby.Settings.MapPath}";
            _gameModeLabel.Text = $"Game Mode: {currentLobby.Settings.GameMode}";
            _difficultyLabel.Text = $"Difficulty: {currentLobby.Settings.Difficulty}";

            // Update players list
            _playersList.Clear();
            foreach (var player in currentLobby.Players)
            {
                var leaderIndicator = player.IsLeader ? " (Leader)" : "";
                var readyIndicator = player.IsReady ? " (Ready)" : "";
                _playersList.AddItem($"{player.DisplayName}{leaderIndicator}{readyIndicator}");
            }
        }
    }

    private void _on_ready_pressed()
    {
        var lobbyService = GetNode("/root/LobbyService") as Waterjam.Game.Services.Lobby.LobbyService;
        if (lobbyService == null)
        {
            GD.PushWarning("LobbyService not found");
            return;
        }

        var currentLobby = lobbyService.GetCurrentPlayerLobby();
        if (currentLobby == null)
            return;

        var localPlayer = currentLobby.GetPlayer(_localPlayerId);
        if (localPlayer == null)
            return;

        GameEvent.DispatchGlobal(new SetPlayerReadyRequestEvent(!localPlayer.IsReady));
    }

    private void _on_leave_lobby_pressed()
    {
        GameEvent.DispatchGlobal(new LeaveLobbyRequestEvent());
    }

    private void _on_change_leader_pressed()
    {
        var lobbyService = GetNode("/root/GameSystems/LobbyService") as Waterjam.Game.Services.Lobby.LobbyService;
        if (lobbyService == null)
        {
            GD.PushWarning("LobbyService not found");
            return;
        }

        var currentLobby = lobbyService.GetCurrentPlayerLobby();
        if (currentLobby == null)
            return;

        var otherPlayers = currentLobby.Players.Where(p => p.PlayerId != _localPlayerId).ToList();
        if (otherPlayers.Count == 0)
        {
            GD.Print("No other players to promote to leader");
            return;
        }

        var dialog = new AcceptDialog();
        dialog.Title = "Change Leader";
        dialog.DialogText = "Select new leader:";
        dialog.AddCancelButton("Cancel");

        var optionButton = new OptionButton();
        foreach (var player in otherPlayers)
        {
            optionButton.AddItem(player.DisplayName);
        }
        dialog.AddChild(optionButton);

        dialog.Confirmed += () =>
        {
            var selectedIndex = optionButton.Selected;
            if (selectedIndex >= 0 && selectedIndex < otherPlayers.Count)
            {
                var newLeaderId = otherPlayers[selectedIndex].PlayerId;
                GameEvent.DispatchGlobal(new ChangeLobbyLeaderRequestEvent(newLeaderId));
            }
        };

        AddChild(dialog);
        dialog.PopupCentered();
    }

    private void _on_start_game_pressed()
    {
        GameEvent.DispatchGlobal(new StartGameRequestEvent());
    }

    private void _on_close_pressed()
    {
        GameEvent.DispatchGlobal(new UiShowMainMenuEvent());
        QueueFree();
    }

    // Event handlers
    public void OnGameEvent(LobbyCreatedEvent eventArgs)
    {
        CallDeferred(nameof(UpdateLobbyDisplay));
    }

    public void OnGameEvent(LobbyJoinedEvent eventArgs)
    {
        CallDeferred(nameof(UpdateLobbyDisplay));
    }

    public void OnGameEvent(LobbyLeftEvent eventArgs)
    {
        CallDeferred(nameof(UpdateLobbyDisplay));
    }

    public void OnGameEvent(LobbyPlayerJoinedEvent eventArgs)
    {
        CallDeferred(nameof(UpdateLobbyDisplay));
    }

    public void OnGameEvent(LobbyPlayerLeftEvent eventArgs)
    {
        CallDeferred(nameof(UpdateLobbyDisplay));
    }

    public void OnGameEvent(LobbyLeaderChangedEvent eventArgs)
    {
        CallDeferred(nameof(UpdateLobbyDisplay));
    }

    public void OnGameEvent(LobbySettingsChangedEvent eventArgs)
    {
        CallDeferred(nameof(UpdateLobbyDisplay));
    }

    public void OnGameEvent(LobbyStartedEvent eventArgs)
    {
        CallDeferred(nameof(UpdateLobbyDisplay));
    }

    public void OnGameEvent(LobbyEndedEvent eventArgs)
    {
        CallDeferred(nameof(UpdateLobbyDisplay));
    }

    public void OnGameEvent(LobbyPlayerReadyChangedEvent eventArgs)
    {
        CallDeferred(nameof(UpdateLobbyDisplay));
    }
}
