using Godot;
using Waterjam.Core.Systems.Console;
using Waterjam.Events;

namespace Waterjam.Core.Services;

public partial class SoundService : BaseService,
    IGameEventHandler<SoundPlaySfxEvent>
{
    public const string SfxCoinStreamPath = "res://resources/sounds/coin.wav";
    public const string SfxExplosionStreamPath = "res://resources/sounds/explosion.wav";
    public const string SfxHurtStreamPath = "res://resources/sounds/hurt.wav";
    public const string SfxJumpStreamPath = "res://resources/sounds/jump.wav";
    public const string SfxPowerUpStreamPath = "res://resources/sounds/power_up.wav";
    public const string SfxTapStreamPath = "res://resources/sounds/tap.wav";

    public void PlaySfx(string streamPath, float volumeDb = -12.0f)
    {
        var player = GetAvailableSfxPlayer();
        if (player == null)
        {
            ConsoleSystem.LogErr("No available SFX players!", ConsoleChannel.Audio);
            return;
        }

        var stream = GD.Load<AudioStream>(streamPath);
        if (stream == null)
        {
            ConsoleSystem.LogErr($"Failed to load audio stream: {streamPath}", ConsoleChannel.Audio);
            return;
        }

        player.Stream = stream;

        // Store the base volume as metadata for volume adjustments
        player.SetMeta("baseVolumeDb", volumeDb);

        // Calculate the final volume
        float combinedVolumeDb;
        if (_sfxVolume <= 0)
        {
            combinedVolumeDb = -80.0f; // Effectively muted
        }
        else
        {
            // Convert base volume to linear scale (0-1)
            var baseDb = volumeDb;

            // Calculate the final dB value by adding the log of the scale factor
            combinedVolumeDb = baseDb + Mathf.LinearToDb(_sfxVolume);
        }

        player.VolumeDb = combinedVolumeDb;

        // ConsoleSystem.Log($"Playing audio stream: {streamPath} at volume: {combinedVolumeDb:F2}dB (settings: {_sfxVolume}, sound: {volumeDb})", ConsoleChannel.Audio);
        player.Play();
    }

    public void OnGameEvent(SoundPlaySfxEvent eventArgs)
    {
        PlaySfx(eventArgs.StreamPath, eventArgs.VolumeDb);
    }
}