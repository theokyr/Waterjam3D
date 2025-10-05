using System.Collections.Generic;
using Waterjam.Events;
using Godot;
using Waterjam.Core.Systems.Console;

namespace Waterjam.Core.Services;

public partial class SoundService : BaseService,
    IGameEventHandler<AudioSettingsChangedEvent>,
    IGameEventHandler<GameInitializedEvent>
{
    protected AudioStreamPlayer _musicChannel;
    protected readonly List<AudioStreamPlayer> _sfxPool = new();
    protected readonly Queue<AudioStreamPlayer> _availableSfxPlayers = new();
    protected float _musicVolume = 0.20222f;
    protected float _sfxVolume = 0.14222f;

    protected const int INITIAL_SFX_POOL_SIZE = 8;
    protected const int MAX_SFX_POOL_SIZE = 16;

    public override void _Ready()
    {
        if (_musicChannel == null)
        {
            _musicChannel = new AudioStreamPlayer();
            AddChild(_musicChannel);
        }

        InitializeSfxPool();
        ConsoleSystem.Log("SoundService Ready!", ConsoleChannel.Audio);
    }

    private void InitializeSfxPool()
    {
        // Create initial pool of SFX players
        for (var i = 0; i < INITIAL_SFX_POOL_SIZE; i++) CreateSfxPlayer();
    }

    protected void CreateSfxPlayer()
    {
        var player = new AudioStreamPlayer();
        AddChild(player);
        _sfxPool.Add(player);
        _availableSfxPlayers.Enqueue(player);

        // When the sound finishes, return the player to the available queue
        player.Finished += () => { _availableSfxPlayers.Enqueue(player); };
    }

    protected AudioStreamPlayer GetAvailableSfxPlayer()
    {
        // If we have an available player, use it
        if (_availableSfxPlayers.Count > 0) return _availableSfxPlayers.Dequeue();

        // If we haven't reached max pool size, create a new player
        if (_sfxPool.Count < MAX_SFX_POOL_SIZE)
        {
            CreateSfxPlayer();
            return _availableSfxPlayers.Dequeue();
        }

        // If we've reached the limit, find the oldest playing sound and reuse it
        foreach (var player in _sfxPool)
            if (!player.Playing)
                return player;

        // If all players are active, use the first one (oldest)
        return _sfxPool[0];
    }

    public void OnGameEvent(GameInitializedEvent eventArgs)
    {
        GameEvent.DispatchGlobal(new SettingsRequestedEvent());
        // this.Dispatch(new SoundPlayMusicEvent(MusicThemeStreamPath)); FIXME: Enable music
    }

    public void OnGameEvent(AudioSettingsChangedEvent eventArgs)
    {
        _musicVolume = eventArgs.MusicVolume;
        _sfxVolume = eventArgs.SfxVolume;

        ApplyVolumeSettings();

        ConsoleSystem.Log($"Audio settings updated - Music: {_musicVolume}, SFX: {_sfxVolume}", ConsoleChannel.Audio);
    }

    private void ApplyVolumeSettings()
    {
        if (_musicChannel != null) _musicChannel.VolumeDb = Mathf.LinearToDb(_musicVolume);

        // Update volume for all SFX players
        foreach (var player in _sfxPool)
            if (player.Playing)
            {
                var currentVolumeDb = player.VolumeDb;
                var currentLinear = Mathf.DbToLinear(currentVolumeDb);
                var settingsLinear = Mathf.DbToLinear(_sfxVolume);
                player.VolumeDb = Mathf.LinearToDb(currentLinear * settingsLinear);
            }
    }
}