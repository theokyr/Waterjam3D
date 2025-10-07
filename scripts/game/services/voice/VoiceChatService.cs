using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Waterjam.Core.Services;
using Waterjam.Core.Systems.Console;
using Waterjam.Events;
using Waterjam.Game;

namespace Waterjam.Game.Services.Voice;

/// <summary>
/// Service for managing voice chat including proximity chat and voice settings.
/// </summary>
public partial class VoiceChatService : BaseService,
    IGameEventHandler<GameInitializedEvent>
{
    private const float PROXIMITY_RANGE = 50.0f; // Distance for proximity voice chat
    private const float UPDATE_INTERVAL = 0.1f; // Update voice connections every 100ms

    private Dictionary<string, VoiceChatPlayer> _voicePlayers = new();
    private Dictionary<string, HashSet<string>> _proximityGroups = new(); // playerId -> set of nearby playerIds
    private Timer _updateTimer;
    private float _updateAccumulator = 0f;

    private VoiceSettings _settings;
    private string _localPlayerId;

    // Audio capture and playback
    private AudioEffectCapture _audioCapture;
    private AudioStreamPlayer _audioPlayer;
    private Dictionary<string, AudioStreamPlayer> _playerAudioStreams = new();

    public override void _Ready()
    {
        base._Ready();
        ConsoleSystem.Log("VoiceChatService initialized", ConsoleChannel.Game);

        // Set up update timer
        _updateTimer = new Timer();
        _updateTimer.WaitTime = UPDATE_INTERVAL;
        _updateTimer.OneShot = false;
        _updateTimer.Timeout += OnUpdateTimerTimeout;
        AddChild(_updateTimer);

        // Set up audio capture and playback
        SetupAudio();

        // Load voice settings
        LoadVoiceSettings();

        // Register console commands for debugging
        RegisterConsoleCommands();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);

        // Update proximity detection
        _updateAccumulator += (float)delta;
        if (_updateAccumulator >= UPDATE_INTERVAL)
        {
            _updateAccumulator = 0f;
            UpdateProximityGroups();
        }

        // Handle voice activation detection
        if (_settings.VoiceMode == VoiceMode.VoiceActivation)
        {
            CheckVoiceActivation();
        }
    }

    public override void _Input(InputEvent @event)
    {
        base._Input(@event);

        // Handle push-to-talk input
        if (_settings.VoiceMode == VoiceMode.PushToTalk && @event is InputEventKey keyEvent)
        {
            if (keyEvent.Keycode == _settings.PushToTalkKey)
            {
                if (keyEvent.Pressed && !keyEvent.Echo)
                {
                    StartVoiceTransmission();
                }
                else if (!keyEvent.Pressed)
                {
                    StopVoiceTransmission();
                }
            }
        }
    }

    /// <summary>
    /// Sets the local player ID.
    /// </summary>
    public void SetLocalPlayerId(string playerId)
    {
        _localPlayerId = playerId;
        ConsoleSystem.Log($"Local player ID set to: {playerId}", ConsoleChannel.Game);
    }

    private void SetupAudio()
    {
        // Create audio capture effect
        _audioCapture = new AudioEffectCapture();
        _audioCapture.BufferLength = 0.1f; // 100ms buffer

        // Create master bus with capture effect
        var masterBus = AudioServer.GetBusIndex("Master");
        AudioServer.AddBusEffect(masterBus, _audioCapture);

        // Create audio player for voice playback
        _audioPlayer = new AudioStreamPlayer();
        _audioPlayer.VolumeDb = -10; // Lower volume for voice chat
        AddChild(_audioPlayer);

        ConsoleSystem.Log("Voice chat audio setup complete", ConsoleChannel.Game);
    }


    /// <summary>
    /// Registers a player for voice chat from network service.
    /// </summary>
    public void RegisterNetworkPlayer(long peerId, PlayerEntity playerEntity)
    {
        var playerId = peerId.ToString();

        if (_voicePlayers.ContainsKey(playerId))
        {
            // Update existing player
            _voicePlayers[playerId].Position = playerEntity.GlobalPosition;
            _voicePlayers[playerId].PlayerNode = playerEntity;
            _voicePlayers[playerId].IsOnline = true;
        }
        else
        {
            // Create new player
            var voicePlayer = new VoiceChatPlayer
            {
                PlayerId = playerId,
                Position = playerEntity.GlobalPosition,
                PlayerNode = playerEntity,
                IsOnline = true,
                IsMuted = false,
                IsTalking = false,
                LastSeen = DateTime.UtcNow
            };

            _voicePlayers[playerId] = voicePlayer;
            _proximityGroups[playerId] = new HashSet<string>();

            // Create audio stream for this player
            var audioStream = new AudioStreamPlayer();
            audioStream.VolumeDb = -15; // Voice chat volume
            audioStream.Bus = "Voice";
            AddChild(audioStream);
            _playerAudioStreams[playerId] = audioStream;

            ConsoleSystem.Log($"Registered network voice player: {playerId}", ConsoleChannel.Game);
        }
    }

    /// <summary>
    /// Unregisters a player from voice chat.
    /// </summary>
    public void UnregisterNetworkPlayer(long peerId)
    {
        var playerId = peerId.ToString();

        if (_voicePlayers.ContainsKey(playerId))
        {
            _voicePlayers.Remove(playerId);
            _proximityGroups.Remove(playerId);

            // Remove audio stream
            if (_playerAudioStreams.TryGetValue(playerId, out var audioStream))
            {
                audioStream.QueueFree();
                _playerAudioStreams.Remove(playerId);
            }

            ConsoleSystem.Log($"Unregistered network voice player: {playerId}", ConsoleChannel.Game);
        }
    }

    /// <summary>
    /// Legacy method for registering players (kept for compatibility).
    /// </summary>
    public void RegisterPlayer(string playerId, Vector3 position, Node3D playerNode)
    {
        if (_voicePlayers.ContainsKey(playerId))
        {
            // Update existing player
            _voicePlayers[playerId].Position = position;
            _voicePlayers[playerId].PlayerNode = playerNode;
            _voicePlayers[playerId].IsOnline = true;
        }
        else
        {
            // Create new player
            var voicePlayer = new VoiceChatPlayer
            {
                PlayerId = playerId,
                Position = position,
                PlayerNode = playerNode,
                IsOnline = true,
                IsMuted = false,
                IsTalking = false,
                LastSeen = DateTime.UtcNow
            };

            _voicePlayers[playerId] = voicePlayer;
            _proximityGroups[playerId] = new HashSet<string>();

            ConsoleSystem.Log($"Registered voice player: {playerId}", ConsoleChannel.Game);
        }
    }

    /// <summary>
    /// Unregisters a player from voice chat.
    /// </summary>
    public void UnregisterPlayer(string playerId)
    {
        if (_voicePlayers.TryGetValue(playerId, out var voicePlayer))
        {
            voicePlayer.IsOnline = false;
            voicePlayer.PlayerNode = null;

            // Remove from all proximity groups
            foreach (var group in _proximityGroups.Values)
            {
                group.Remove(playerId);
            }
            _proximityGroups.Remove(playerId);

            ConsoleSystem.Log($"Unregistered voice player: {playerId}", ConsoleChannel.Game);
        }
    }

    /// <summary>
    /// Updates a player's position for proximity calculations.
    /// </summary>
    public void UpdatePlayerPosition(string playerId, Vector3 position)
    {
        if (_voicePlayers.TryGetValue(playerId, out var voicePlayer))
        {
            voicePlayer.Position = position;
            voicePlayer.LastSeen = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Sets a player's talking state.
    /// </summary>
    public void SetPlayerTalking(string playerId, bool isTalking)
    {
        if (_voicePlayers.TryGetValue(playerId, out var voicePlayer))
        {
            voicePlayer.IsTalking = isTalking;

            if (isTalking)
            {
                GameEvent.DispatchGlobal(new VoicePlayerStartedTalkingEvent(playerId));
            }
            else
            {
                GameEvent.DispatchGlobal(new VoicePlayerStoppedTalkingEvent(playerId));
            }
        }
    }

    /// <summary>
    /// Sets a player's mute state.
    /// </summary>
    public void SetPlayerMuted(string playerId, bool isMuted)
    {
        if (_voicePlayers.TryGetValue(playerId, out var voicePlayer))
        {
            voicePlayer.IsMuted = isMuted;
            GameEvent.DispatchGlobal(new VoicePlayerMutedEvent(playerId, isMuted));
        }
    }

    /// <summary>
    /// Updates proximity groups based on player positions and network service.
    /// </summary>
    private void UpdateProximityGroups()
    {
        if (string.IsNullOrEmpty(_localPlayerId)) return;

        var networkService = GetNodeOrNull<NetworkService>("/root/NetworkService");
        if (networkService == null) return;

        // Get local player entity
        var localPlayer = networkService.GetPlayerEntity(long.Parse(_localPlayerId));
        if (localPlayer == null) return;

        // Update proximity for all players using network service data
        foreach (var kvp in networkService.NetworkedPlayers)
        {
            var peerId = kvp.Key;
            var playerEntity = kvp.Value;
            var playerId = peerId.ToString();

            // Skip if this is the local player
            if (playerId == _localPlayerId) continue;

            // Calculate distance
            var distance = localPlayer.GlobalPosition.DistanceTo(playerEntity.GlobalPosition);

            // Check if player is in proximity range
            var shouldBeConnected = distance <= PROXIMITY_RANGE;

            // Update proximity group
            if (shouldBeConnected)
            {
                if (!_proximityGroups[_localPlayerId].Contains(playerId))
                {
                    ConnectVoiceProximity(_localPlayerId, playerId);
                }
            }
            else
            {
                if (_proximityGroups[_localPlayerId].Contains(playerId))
                {
                    DisconnectVoiceProximity(_localPlayerId, playerId);
                }
            }
        }
    }

    /// <summary>
    /// Connects two players for voice proximity chat.
    /// </summary>
    private void ConnectVoiceProximity(string playerId1, string playerId2)
    {
        // Add to proximity groups
        if (!_proximityGroups.ContainsKey(playerId1))
            _proximityGroups[playerId1] = new HashSet<string>();
        if (!_proximityGroups.ContainsKey(playerId2))
            _proximityGroups[playerId2] = new HashSet<string>();

        _proximityGroups[playerId1].Add(playerId2);
        _proximityGroups[playerId2].Add(playerId1);

        ConsoleSystem.Log($"Voice proximity connected: {playerId1} <-> {playerId2}", ConsoleChannel.Game);

        // Dispatch event
        GameEvent.DispatchGlobal(new VoiceProximityConnectedEvent(playerId1, playerId2));
    }

    /// <summary>
    /// Disconnects two players from voice proximity chat.
    /// </summary>
    private void DisconnectVoiceProximity(string playerId1, string playerId2)
    {
        // Remove from proximity groups
        if (_proximityGroups.ContainsKey(playerId1))
            _proximityGroups[playerId1].Remove(playerId2);
        if (_proximityGroups.ContainsKey(playerId2))
            _proximityGroups[playerId2].Remove(playerId1);

        ConsoleSystem.Log($"Voice proximity disconnected: {playerId1} <-> {playerId2}", ConsoleChannel.Game);

        // Dispatch event
        GameEvent.DispatchGlobal(new VoiceProximityDisconnectedEvent(playerId1, playerId2));
    }

    /// <summary>
    /// Gets players within proximity range of a specific player.
    /// </summary>
    public IReadOnlyCollection<string> GetNearbyPlayers(string playerId)
    {
        if (_proximityGroups.TryGetValue(playerId, out var nearby))
        {
            return nearby.Where(p => _voicePlayers.TryGetValue(p, out var player) && player.IsOnline).ToList();
        }
        return new List<string>();
    }

    /// <summary>
    /// Gets all registered voice players.
    /// </summary>
    public IReadOnlyDictionary<string, VoiceChatPlayer> GetAllVoicePlayers()
    {
        return _voicePlayers.Where(p => p.Value.IsOnline).ToDictionary(p => p.Key, p => p.Value);
    }

    /// <summary>
    /// Loads voice chat settings.
    /// </summary>
    private void LoadVoiceSettings()
    {
        _settings = new VoiceSettings();

        // Try to load from settings service if available
        var settingsService = GetNodeOrNull("/root/SettingsService") as Waterjam.Core.Services.SettingsService;
        if (settingsService != null)
        {
            // Load voice settings using public methods
            _settings.PushToTalkKey = (Key)(int)settingsService.GetVoiceSetting("push_to_talk_key", (int)Key.T);
            _settings.VoiceMode = (VoiceMode)(int)settingsService.GetVoiceSetting("voice_mode", (int)VoiceMode.PushToTalk);
            _settings.MasterVolume = (float)settingsService.GetVoiceSetting("master_volume", 0.8f);
            _settings.VoiceActivationThreshold = (float)settingsService.GetVoiceSetting("voice_activation_threshold", -40.0f);
            _settings.ProximityRange = (float)settingsService.GetVoiceSetting("proximity_range", PROXIMITY_RANGE);
        }
        else
        {
            // Use defaults
            _settings.PushToTalkKey = Key.T;
            _settings.VoiceMode = VoiceMode.PushToTalk;
            _settings.MasterVolume = 0.8f;
            _settings.VoiceActivationThreshold = -40.0f;
            _settings.ProximityRange = PROXIMITY_RANGE;
        }

        ConsoleSystem.Log($"Voice settings loaded: Mode={_settings.VoiceMode}, PTT Key={_settings.PushToTalkKey}", ConsoleChannel.Game);
    }

    /// <summary>
    /// Saves voice chat settings.
    /// </summary>
    private void SaveVoiceSettings()
    {
        var settingsService = GetNodeOrNull("/root/SettingsService") as Waterjam.Core.Services.SettingsService;
        if (settingsService != null)
        {
            settingsService.SetVoiceSetting("push_to_talk_key", (int)_settings.PushToTalkKey);
            settingsService.SetVoiceSetting("voice_mode", (int)_settings.VoiceMode);
            settingsService.SetVoiceSetting("master_volume", _settings.MasterVolume);
            settingsService.SetVoiceSetting("voice_activation_threshold", _settings.VoiceActivationThreshold);
            settingsService.SetVoiceSetting("proximity_range", _settings.ProximityRange);
        }
    }

    /// <summary>
    /// Updates voice settings.
    /// </summary>
    public void UpdateVoiceSettings(VoiceSettings newSettings)
    {
        var oldSettings = _settings;
        _settings = newSettings;

        SaveVoiceSettings();

        // Notify about settings change
        GameEvent.DispatchGlobal(new VoiceSettingsChangedEvent(oldSettings, newSettings));
    }

    /// <summary>
    /// Gets current voice settings.
    /// </summary>
    public VoiceSettings GetVoiceSettings()
    {
        return _settings.Clone();
    }

    /// <summary>
    /// Starts voice transmission for the local player.
    /// </summary>
    private void StartVoiceTransmission()
    {
        if (string.IsNullOrEmpty(_localPlayerId)) return;

        if (_voicePlayers.TryGetValue(_localPlayerId, out var localPlayer))
        {
            if (!localPlayer.IsTalking)
            {
                localPlayer.IsTalking = true;
                ConsoleSystem.Log("Voice transmission started", ConsoleChannel.Game);
                GameEvent.DispatchGlobal(new VoicePlayerStartedTalkingEvent(_localPlayerId));
            }
        }
    }

    /// <summary>
    /// Stops voice transmission for the local player.
    /// </summary>
    private void StopVoiceTransmission()
    {
        if (string.IsNullOrEmpty(_localPlayerId)) return;

        if (_voicePlayers.TryGetValue(_localPlayerId, out var localPlayer))
        {
            if (localPlayer.IsTalking)
            {
                localPlayer.IsTalking = false;
                ConsoleSystem.Log("Voice transmission stopped", ConsoleChannel.Game);
                GameEvent.DispatchGlobal(new VoicePlayerStoppedTalkingEvent(_localPlayerId));
            }
        }
    }

    /// <summary>
    /// Checks voice activation threshold for automatic voice detection.
    /// </summary>
    private void CheckVoiceActivation()
    {
        if (string.IsNullOrEmpty(_localPlayerId) || _audioCapture == null) return;

        // Get audio data from capture
        var audioData = _audioCapture.GetBuffer(_audioCapture.GetFramesAvailable());
        if (audioData.Length == 0) return;

        // Calculate RMS (Root Mean Square) for stereo samples (Vector2 per frame)
        double sumSquares = 0.0;
        for (int i = 0; i < audioData.Length; i++)
        {
            var s = audioData[i];
            sumSquares += (double)s.X * (double)s.X;
            sumSquares += (double)s.Y * (double)s.Y;
        }
        int totalSamples = audioData.Length * 2; // left + right per frame
        float rms = totalSamples > 0 ? (float)Math.Sqrt(sumSquares / totalSamples) : 0f;

        // Convert to dB
        var volumeDb = rms > 0 ? 20f * (float)Math.Log10(rms) : -60f;

        // Check if voice should be activated/deactivated
        var shouldBeTalking = volumeDb > _settings.VoiceActivationThreshold;

        if (_voicePlayers.TryGetValue(_localPlayerId, out var localPlayer))
        {
            if (shouldBeTalking && !localPlayer.IsTalking)
            {
                StartVoiceTransmission();
            }
            else if (!shouldBeTalking && localPlayer.IsTalking)
            {
                StopVoiceTransmission();
            }
        }
    }

    public void OnGameEvent(GameInitializedEvent eventArgs)
    {
        _updateTimer.Start();
    }

    private void OnUpdateTimerTimeout()
    {
        // Timer-based updates for proximity detection
    }

    private void RegisterConsoleCommands()
    {
        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "voice_settings",
            "Show current voice settings",
            "voice_settings",
            async (args) =>
            {
                ConsoleSystem.Log($"Voice Mode: {_settings.VoiceMode}", ConsoleChannel.Game);
                ConsoleSystem.Log($"PTT Key: {_settings.PushToTalkKey}", ConsoleChannel.Game);
                ConsoleSystem.Log($"Master Volume: {_settings.MasterVolume}", ConsoleChannel.Game);
                ConsoleSystem.Log($"Voice Activation Threshold: {_settings.VoiceActivationThreshold}", ConsoleChannel.Game);
                ConsoleSystem.Log($"Proximity Range: {_settings.ProximityRange}", ConsoleChannel.Game);
                ConsoleSystem.Log($"Online Players: {_voicePlayers.Count(p => p.Value.IsOnline)}", ConsoleChannel.Game);
                return true;
            }));

        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "voice_set_mode",
            "Set voice mode (0=AlwaysOn, 1=PushToTalk, 2=VoiceActivation)",
            "voice_set_mode <mode>",
            async (args) =>
            {
                if (args.Length == 0 || !int.TryParse(args[0], out var modeInt))
                {
                    ConsoleSystem.Log("Usage: voice_set_mode <mode> (0=AlwaysOn, 1=PushToTalk, 2=VoiceActivation)", ConsoleChannel.Game);
                    return false;
                }

                var mode = (VoiceMode)modeInt;
                if (mode < VoiceMode.AlwaysOn || mode > VoiceMode.VoiceActivation)
                {
                    ConsoleSystem.Log("Invalid mode. Use 0=AlwaysOn, 1=PushToTalk, 2=VoiceActivation", ConsoleChannel.Game);
                    return false;
                }

                var newSettings = _settings.Clone();
                newSettings.VoiceMode = mode;
                UpdateVoiceSettings(newSettings);

                ConsoleSystem.Log($"Voice mode set to: {mode}", ConsoleChannel.Game);
                return true;
            }));
    }
}

/// <summary>
/// Represents a player in the voice chat system.
/// </summary>
public class VoiceChatPlayer
{
    public string PlayerId { get; set; }
    public Vector3 Position { get; set; }
    public Node3D PlayerNode { get; set; }
    public bool IsOnline { get; set; }
    public bool IsTalking { get; set; }
    public bool IsMuted { get; set; }
    public DateTime LastSeen { get; set; }
}

/// <summary>
/// Voice chat settings.
/// </summary>
public class VoiceSettings
{
    public Key PushToTalkKey { get; set; } = Key.T;
    public VoiceMode VoiceMode { get; set; } = VoiceMode.PushToTalk;
    public float MasterVolume { get; set; } = 0.8f;
    public float VoiceActivationThreshold { get; set; } = -40.0f; // dB
    public float ProximityRange { get; set; } = 50.0f;

    public VoiceSettings Clone()
    {
        return new VoiceSettings
        {
            PushToTalkKey = PushToTalkKey,
            VoiceMode = VoiceMode,
            MasterVolume = MasterVolume,
            VoiceActivationThreshold = VoiceActivationThreshold,
            ProximityRange = ProximityRange
        };
    }
}

/// <summary>
/// Voice chat modes.
/// </summary>
public enum VoiceMode
{
    AlwaysOn,
    PushToTalk,
    VoiceActivation
}
