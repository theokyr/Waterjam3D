using Godot;
using Waterjam.Core.Systems.Console;
using Waterjam.Events;

namespace Waterjam.Core.Services;

public partial class SoundService : BaseService,
    IGameEventHandler<SoundPlayMusicEvent>
{
    public const string MusicThemeStreamPath = "res://resources/music/theme.mp3";

    public void PlayMusic(string streamPath, float volumeDb = -12.0f)
    {
        if (_musicChannel == null)
        {
            ConsoleSystem.LogErr("Music channel is null. Make sure _Ready() is called.", ConsoleChannel.Audio);
            return;
        }

        if (_musicChannel.Playing) _musicChannel.Stop();

        var stream = GD.Load<AudioStream>(streamPath);
        if (stream == null)
        {
            ConsoleSystem.LogErr($"Failed to load audio stream (file may be missing): {streamPath}", ConsoleChannel.Audio);
            return;
        }

        _musicChannel.Stream = stream;
        _musicChannel.VolumeDb = Mathf.LinearToDb(_musicVolume);

        ConsoleSystem.Log($"Playing music: {streamPath} at volume: {_musicChannel.VolumeDb}dB", ConsoleChannel.Audio);
        _musicChannel.Play();
    }

    public void OnGameEvent(SoundPlayMusicEvent eventArgs)
    {
        PlayMusic(eventArgs.StreamPath, eventArgs.VolumeDb);
    }
}