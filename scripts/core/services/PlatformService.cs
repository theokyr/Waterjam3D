using Godot;
using GodotSteam;
using Waterjam.Core.Systems.Console;
using Waterjam.Core.Services.Platform;
using System;
using Godot.Collections;

namespace Waterjam.Core.Services;

/// <summary>
/// Provides a unified platform adapter (Steam, Epic, custom P2P, Null) to the game.
/// Chooses the best available adapter at runtime and exposes shared facilities.
/// </summary>
public partial class PlatformService : BaseService
{
    public static bool IsSteamInitialized { get; private set; }
    public static SteamInitStatus? LastSteamInitStatus { get; private set; }

    public IPlatformAdapter Adapter { get; private set; } = new NullPlatformAdapter();

    public string PlatformName => Adapter.PlatformName;
    public ICloudStorage Cloud => Adapter.Cloud;
    public IAchievementPlatform Achievements => Adapter.Achievements;

    public override void _Ready()
    {
        base._Ready();

        // Initialize Steam via GodotSteam C# bindings (typed path with fallbacks)
        try
        {
            if (!ClassDB.ClassExists("Steam") || !ClassDB.CanInstantiate("Steam"))
            {
                IsSteamInitialized = false;
                Adapter = new NullPlatformAdapter();
                ConsoleSystem.LogWarn("Steam extension not loaded; using Null platform.", ConsoleChannel.Game);
            }
            else
            {
                // Determine App ID and auto-init flags from ProjectSettings, fallback to defaults
                uint appId = 480;
                bool autoInit = false;
                try
                {
                    // New-style key
                    if (ProjectSettings.HasSetting("steam/initialization/app_id"))
                    {
                        var s = ProjectSettings.GetSetting("steam/initialization/app_id").ToString();
                        if (!string.IsNullOrEmpty(s) && uint.TryParse(s.Trim(), out var parsed))
                        {
                            appId = parsed;
                        }
                    }
                    // Legacy key
                    else if (ProjectSettings.HasSetting("steam/app_id"))
                    {
                        var s = ProjectSettings.GetSetting("steam/app_id").ToString();
                        if (!string.IsNullOrEmpty(s) && uint.TryParse(s.Trim(), out var parsed))
                        {
                            appId = parsed;
                        }
                    }

                    if (ProjectSettings.HasSetting("steam/initialization/initialize_on_startup"))
                    {
                        var s = ProjectSettings.GetSetting("steam/initialization/initialize_on_startup").ToString();
                        if (!string.IsNullOrEmpty(s) && bool.TryParse(s.Trim(), out var b))
                        {
                            autoInit = b;
                        }
                    }
                }
                catch { }

                // Optional: comply with Steam bootstrap in shipped builds
                if (!autoInit)
                {
                    try
                    {
                        var needsRestart = Steam.RestartAppIfNecessary(appId);
                        ConsoleSystem.Log($"Steam.RestartAppIfNecessary({appId}) => {needsRestart}", ConsoleChannel.Game);
                        if (needsRestart && !Engine.IsEditorHint())
                        {
                            ConsoleSystem.LogWarn("Restart requested by Steam; quitting to relaunch under Steam.", ConsoleChannel.Game);
                            GetTree().Quit();
                            return;
                        }
                    }
                    catch (Exception restartEx)
                    {
                        ConsoleSystem.LogWarn($"RestartAppIfNecessary failed: {restartEx.Message}", ConsoleChannel.Game);
                    }
                }

                ConsoleSystem.Log($"Steam.IsSteamRunning() => {Steam.IsSteamRunning()}", ConsoleChannel.Game);

                SteamInitStatus status = SteamInitStatus.SteamworksFailedToInitialize;
                string verbal = string.Empty;

                if (!autoInit)
                {
                    try
                    {
                        var init = Steam.SteamInit(true);
                        status = init.Status;
                        verbal = init.Verbal ?? string.Empty;
                    }
                    catch (Exception typedEx)
                    {
                        // If typed path fails due to payload mismatch, try extended init
                        try
                        {
                            var initEx = Steam.SteamInitEx(true);
                            status = (SteamInitStatus)(int)initEx.Status; // cast to closest enum for logging
                            verbal = initEx.Verbal ?? string.Empty;
                        }
                        catch (Exception ex2)
                        {
                            IsSteamInitialized = false;
                            Adapter = new NullPlatformAdapter();
                            LastSteamInitStatus = null;
                            ConsoleSystem.LogWarn($"Steam init failed: {typedEx.Message}; fallback failed: {ex2.Message}", ConsoleChannel.Game);
                            goto AfterInit;
                        }
                    }
                }

                LastSteamInitStatus = status;
				// Consider Steam ready primarily by runtime checks; some bindings return 0 for success in SteamInitEx
				bool loggedOn = false;
				ulong steamId = 0;
				try
				{
					loggedOn = Steam.LoggedOn();
					steamId = Steam.GetSteamID();
				}
				catch (Exception checkEx)
				{
					ConsoleSystem.LogWarn($"Steam post-init checks failed: {checkEx.Message}", ConsoleChannel.Game);
				}

                if (loggedOn && steamId != 0)
                {
                    IsSteamInitialized = true;
                    Adapter = new SteamPlatformAdapter();
                    ConsoleSystem.Log($"Steam initialized. Status={status}; App ID: {appId}; LoggedOn={loggedOn}; SteamID={steamId}; {verbal}", ConsoleChannel.Game);
                    
                    // Wire up Steam invite callback
                    try
                    {
                        Steam.JoinRequested += OnSteamJoinRequested;
                        ConsoleSystem.Log("[PlatformService] Steam JoinRequested callback registered", ConsoleChannel.Game);
                    }
                    catch (Exception callbackEx)
                    {
                        ConsoleSystem.LogWarn($"[PlatformService] Failed to register JoinRequested callback: {callbackEx.Message}", ConsoleChannel.Game);
                    }
                }
                else
                {
                    IsSteamInitialized = false;
                    Adapter = new NullPlatformAdapter();
                    ConsoleSystem.LogWarn($"Steam init incomplete: {status}. LoggedOn={loggedOn}; SteamID={steamId}; Details: {verbal}", ConsoleChannel.Game);
                }
            }
        }
        catch (Exception ex)
        {
            IsSteamInitialized = false;
            LastSteamInitStatus = null;
            Adapter = new NullPlatformAdapter();
            ConsoleSystem.LogWarn($"Steam initialization error: {ex.Message}", ConsoleChannel.Game);
        }
        AfterInit:

        ConsoleSystem.Log($"PlatformService ready: {Adapter.PlatformName}", ConsoleChannel.Game);

        RegisterConsoleCommands();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        // Pump Steam callbacks if Steam class is available, even if not fully initialized yet
        try
        {
            if (ClassDB.ClassExists("Steam") && ClassDB.CanInstantiate("Steam"))
            {
                Steam.RunCallbacks();
            }
        }
        catch { }
    }

    private void RegisterConsoleCommands()
    {
        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "platform_info",
            "Show current platform adapter",
            "platform_info",
            async (args) =>
            {
                ConsoleSystem.Log($"Platform: {PlatformName}", ConsoleChannel.Game);
                ConsoleSystem.Log($"Steam initialized: {IsSteamInitialized} (status: {LastSteamInitStatus})", ConsoleChannel.Game);
                ConsoleSystem.Log($"Cloud available: {Cloud?.IsAvailable == true}", ConsoleChannel.Game);
                ConsoleSystem.Log($"Achievements available: {Achievements?.IsAvailable == true}", ConsoleChannel.Game);
                return true;
            }));

        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "steam_status",
            "Show Steam initialization and user status",
            "steam_status",
            async (args) =>
            {
                try
                {
                    var isRunning = ClassDB.ClassExists("Steam") && ClassDB.CanInstantiate("Steam") && Steam.IsSteamRunning();
                    var loggedOn = isRunning && Steam.LoggedOn();
                    var steamId = isRunning ? Steam.GetSteamID() : 0;
                    var appId = isRunning ? GodotSteam.Steam.GetAppIDSafe() : 0;
                    ConsoleSystem.Log($"Steam running: {isRunning}", ConsoleChannel.Game);
                    ConsoleSystem.Log($"Steam logged on: {loggedOn}", ConsoleChannel.Game);
                    ConsoleSystem.Log($"SteamID: {steamId}", ConsoleChannel.Game);
                    ConsoleSystem.Log($"AppID: {appId}", ConsoleChannel.Game);
                }
                catch (System.Exception ex)
                {
                    ConsoleSystem.LogErr($"steam_status error: {ex.Message}", ConsoleChannel.Game);
                }
                return true;
            }));

        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "steam_avatar",
            "Fetch and log avatar load for a SteamID (or self)",
            "steam_avatar [steamId]",
            async (args) =>
            {
                try
                {
                    if (!IsSteamInitialized)
                    {
                        ConsoleSystem.LogWarn("Steam not initialized.", ConsoleChannel.Game);
                        return false;
                    }
                    ulong targetId = 0;
                    if (args.Length > 0 && ulong.TryParse(args[0], out var parsed))
                    {
                        targetId = parsed;
                    }
                    if (targetId == 0)
                    {
                        targetId = Steam.GetSteamID();
                    }

                    // Subscribe once for feedback
                    GodotSteam.Steam.AvatarImageLoaded += OnDebugAvatarLoaded;
                    GodotSteam.Steam.GetPlayerAvatar(GodotSteam.AvatarSize.Medium, targetId);
                    ConsoleSystem.Log($"Requested avatar for {targetId}", ConsoleChannel.Game);
                }
                catch (System.Exception ex)
                {
                    ConsoleSystem.LogErr($"steam_avatar error: {ex.Message}", ConsoleChannel.Game);
                }
                return true;
            }));

        ConsoleSystem.Instance?.RegisterCommand(new ConsoleCommand(
            "steam_reconnect",
            "Retry Steam initialization after an optional delay (seconds)",
            "steam_reconnect [delaySeconds]",
            async (args) =>
            {
                double delaySeconds = 3.0;
                if (args.Length > 0 && double.TryParse(args[0], out var parsedDelay) && parsedDelay >= 0)
                {
                    delaySeconds = parsedDelay;
                }

                ConsoleSystem.Log($"Scheduling Steam reconnect in {delaySeconds:0.##}s...", ConsoleChannel.Game);

                var timer = new Timer();
                timer.OneShot = true;
                timer.WaitTime = delaySeconds;
                AddChild(timer);
                timer.Timeout += () =>
                {
                    TrySteamReconnect();
                    timer.QueueFree();
                };
                timer.Start();

                return true;
            }));
    }

    // Local debug handler for steam_avatar command; unsubscribes after first fire
    private void OnDebugAvatarLoaded(ulong avatarId, uint avatarIndex, uint width, uint height)
    {
        ConsoleSystem.Log($"Avatar loaded: id={avatarId} index={avatarIndex} {width}x{height}", ConsoleChannel.Game);
        try
        {
            GodotSteam.Steam.AvatarImageLoaded -= OnDebugAvatarLoaded;
        }
        catch {}
    }

    private void TrySteamReconnect()
    {
        try
        {
            if (!ClassDB.ClassExists("Steam") || !ClassDB.CanInstantiate("Steam"))
            {
                ConsoleSystem.LogWarn("Steam extension not available; cannot reconnect.", ConsoleChannel.Game);
                return;
            }

            // Respect auto-init; if auto, don't manually re-init
            bool autoInit = false;
            try
            {
                if (ProjectSettings.HasSetting("steam/initialization/initialize_on_startup"))
                {
                    var s = ProjectSettings.GetSetting("steam/initialization/initialize_on_startup").ToString();
                    if (!string.IsNullOrEmpty(s) && bool.TryParse(s.Trim(), out var b))
                    {
                        autoInit = b;
                    }
                }
            }
            catch { }

            // Shutdown any previous session just in case (only if we are doing manual init)
            if (!autoInit)
            {
                try { Steam.SteamShutdown(); } catch {}
            }

            // Attempt a fresh init using the same logic path as _Ready()
            uint appId = 480;
            try
            {
                if (ProjectSettings.HasSetting("steam/app_id"))
                {
                    var v = ProjectSettings.GetSetting("steam/app_id");
                    var s = v.ToString();
                    if (!string.IsNullOrEmpty(s) && uint.TryParse(s.Trim(), out var parsed))
                    {
                        appId = parsed;
                    }
                }
            }
            catch { }

            if (!autoInit)
            {
                try
                {
                    var needsRestart = Steam.RestartAppIfNecessary(appId);
                    ConsoleSystem.Log($"[Reconnect] RestartAppIfNecessary({appId}) => {needsRestart}", ConsoleChannel.Game);
                    if (needsRestart && !Engine.IsEditorHint())
                    {
                        ConsoleSystem.LogWarn("[Reconnect] Restart requested by Steam; quitting to relaunch under Steam.", ConsoleChannel.Game);
                        GetTree().Quit();
                        return;
                    }
                }
                catch (Exception restartEx)
                {
                    ConsoleSystem.LogWarn($"[Reconnect] RestartAppIfNecessary failed: {restartEx.Message}", ConsoleChannel.Game);
                }
            }

            SteamInitStatus status;
            string verbal = string.Empty;
            if (!autoInit)
            {
                try
                {
                    var init = Steam.SteamInit(true);
                    status = init.Status;
                    verbal = init.Verbal ?? string.Empty;
                }
                catch (Exception)
                {
                    var initEx = Steam.SteamInitEx(true);
                    status = (SteamInitStatus)(int)initEx.Status;
                    verbal = initEx.Verbal ?? string.Empty;
                }
            }
            else
            {
                status = SteamInitStatus.SteamworksActive; // rely on auto-init success; runtime checks will confirm
            }

            bool loggedOn = false;
            ulong steamId = 0;
            try
            {
                loggedOn = Steam.LoggedOn();
                steamId = Steam.GetSteamID();
            }
            catch { }

            LastSteamInitStatus = status;
            // Favor runtime checks (loggedOn + steamId) over status code interpretation
            if (loggedOn && steamId != 0)
            {
                IsSteamInitialized = true;
                Adapter = new SteamPlatformAdapter();
                ConsoleSystem.Log($"[Reconnect] Steam OK. LoggedOn={loggedOn}; SteamID={steamId}; {verbal}", ConsoleChannel.Game);
            }
            else
            {
                IsSteamInitialized = false;
                Adapter = new NullPlatformAdapter();
                ConsoleSystem.LogWarn($"[Reconnect] Steam still not ready: {status}. LoggedOn={loggedOn}; SteamID={steamId}; Details: {verbal}", ConsoleChannel.Game);
            }
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[Reconnect] Error: {ex.Message}", ConsoleChannel.Game);
        }
    }

    /// <summary>
    /// Handles Steam invite acceptance - when a player clicks "Join Game" from Steam overlay or accepts an invite
    /// </summary>
    private void OnSteamJoinRequested(ulong lobbyId, ulong friendId)
    {
        ConsoleSystem.Log($"[PlatformService] Steam join requested! Lobby: {lobbyId}, From Friend: {friendId}", ConsoleChannel.Game);
        
        try
        {
            // Join the Steam lobby - this will trigger the LobbyJoined callback in SteamNetworkAdapter
            Steam.JoinLobby(lobbyId);
            ConsoleSystem.Log($"[PlatformService] Joining Steam lobby {lobbyId}...", ConsoleChannel.Game);
        }
        catch (Exception ex)
        {
            ConsoleSystem.LogErr($"[PlatformService] Failed to join lobby via invite: {ex.Message}", ConsoleChannel.Network);
        }
    }
}


