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
	private LineEdit _lobbyCodeInput;
	private Button _joinByCodeButton;

    private string _localPlayerId;
    private bool _startRequested;

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
        
        // Ensure button signals are wired (in case the scene lacks editor connections)
        if (_startGameButton != null) _startGameButton.Pressed += _on_start_game_pressed;

		// Add join-by-code UI at the top
		CreateJoinByCodeUI();

        // Get local player ID from party service (assuming it's set)
        var partyService = GetNode("/root/PartyService") as Waterjam.Game.Services.Party.PartyService;
        if (partyService != null)
        {
            _localPlayerId = partyService.GetLocalPlayerId();
        }

        UpdateLobbyDisplay();
    }

	private void CreateJoinByCodeUI()
	{
		// Create a container for join-by-code UI at the top of the lobby screen
		var titleNode = GetNodeOrNull<Label>("Panel/VBoxContainer/Title");
		if (titleNode == null) return;

		var vbox = titleNode.GetParent() as VBoxContainer;
		if (vbox == null) return;

		// Create HBox for join by code
		var joinContainer = new HBoxContainer();
		joinContainer.Name = "JoinByCodeContainer";
		joinContainer.CustomMinimumSize = new Vector2(0, 40);

		var label = new Label();
		label.Text = "Join Lobby by Code:";
		label.SizeFlagsVertical = SizeFlags.ShrinkCenter;
		joinContainer.AddChild(label);

		_lobbyCodeInput = new LineEdit();
		_lobbyCodeInput.PlaceholderText = "Enter lobby code";
		_lobbyCodeInput.CustomMinimumSize = new Vector2(200, 0);
		_lobbyCodeInput.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		joinContainer.AddChild(_lobbyCodeInput);

		_joinByCodeButton = new Button();
		_joinByCodeButton.Text = "Join";
		_joinByCodeButton.Pressed += OnJoinByCodePressed;
		joinContainer.AddChild(_joinByCodeButton);

		// Insert after title
		var titleIndex = titleNode.GetIndex();
		vbox.AddChild(joinContainer);
		vbox.MoveChild(joinContainer, titleIndex + 1);
	}

	private void OnJoinByCodePressed()
	{
		var code = _lobbyCodeInput.Text.Trim().ToUpper();
		if (string.IsNullOrEmpty(code))
		{
			GD.PushWarning("No lobby code entered");
			return;
		}

		// Dispatch event to join lobby by code
		GameEvent.DispatchGlobal(new JoinLobbyRequestEvent(code));
		Waterjam.Core.Systems.Console.ConsoleSystem.Log($"[LobbyUI] Attempting to join lobby with code: {code}", Waterjam.Core.Systems.Console.ConsoleChannel.UI);
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
        var lobbyService = GetNode("/root/LobbyService") as Waterjam.Game.Services.Lobby.LobbyService;
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
        if (_startRequested)
            return;

        _startRequested = true;
        if (_startGameButton != null)
            _startGameButton.Disabled = true;

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
        // Close lobby UI when the game starts
        CallDeferred(Node.MethodName.QueueFree);
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
