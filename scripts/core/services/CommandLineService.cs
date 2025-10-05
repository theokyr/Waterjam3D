using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Waterjam.Events;
using Waterjam.Core.Systems.Console;

namespace Waterjam.Core.Services;

public partial class CommandLineService : BaseService,
    IGameEventHandler<GameInitializedEvent>,
    IGameEventHandler<PlayerSpawnedEvent>,
    IGameEventHandler<ConsoleCommandRegisteredEvent>
{
    private readonly List<string> _queuedConsoleCommands = new();
    private Godot.Collections.Dictionary<string, Variant> _pendingSettingsOverrides;
    private bool _executionScheduled;
    private double _lastConsoleRegistrationMs;

    public override void _Ready()
    {
        base._Ready();
        TryParseCommandLine();
    }

    private void TryParseCommandLine()
    {
        string[] args;
        try
        {
            // Prefer user args (filters out engine/editor flags)
            args = OS.GetCmdlineUserArgs();
        }
        catch
        {
            args = OS.GetCmdlineArgs();
        }

        if (args == null || args.Length == 0)
            return;

        ConsoleSystem.Log($"[CLI] Args: {string.Join(' ', args)}", ConsoleChannel.System);

        // Collect temporary settings overrides to apply in one shot
        var settings = new Godot.Collections.Dictionary<string, Variant>();

        // Simple state machine for flags that take a value: -w <int>, -h <int>
        string awaitingValueFor = null;

        for (int i = 0; i < args.Length; i++)
        {
            var token = args[i].Trim();
            if (string.IsNullOrEmpty(token)) continue;

            if (awaitingValueFor != null)
            {
                if (awaitingValueFor == "w" && int.TryParse(token, out var w))
                {
                    ApplyResolutionPart(settings, width: w, height: null);
                }
                else if (awaitingValueFor == "h" && int.TryParse(token, out var h))
                {
                    ApplyResolutionPart(settings, width: null, height: h);
                }

                awaitingValueFor = null;
                continue;
            }

            // '+' prefix => console command (possibly with parameters)
            if (token.StartsWith("+"))
            {
                var command = token.Substring(1);
                // If command includes only the name, allow following args until next +/- flag
                var collected = new List<string> { command };
                // Peek ahead for additional plain arguments
                int j = i + 1;
                while (j < args.Length && !string.IsNullOrWhiteSpace(args[j]) && args[j][0] != '-' && args[j][0] != '+')
                {
                    collected.Add(args[j]);
                    j++;
                }

                i = j - 1;
                _queuedConsoleCommands.Add(string.Join(' ', collected));
                continue;
            }

            // '-' prefix => global params
            if (token.StartsWith("-"))
            {
                var flag = token.TrimStart('-').ToLowerInvariant();
                switch (flag)
                {
                    case "windowed":
                    case "w1": // optional alias
                        settings["display/display_mode"] = 0; // Windowed
                        break;
                    case "fullscreen":
                    case "fs":
                        settings["display/display_mode"] = 1; // Fullscreen
                        break;
                    case "exclusive":
                        settings["display/display_mode"] = 2; // Exclusive Fullscreen
                        break;
                    case "w":
                        awaitingValueFor = "w";
                        break;
                    case "h":
                        awaitingValueFor = "h";
                        break;
                    case "vsync":
                        settings["display/vsync"] = true;
                        break;
                    case "novsync":
                        settings["display/vsync"] = false;
                        break;
                    case "skipintro":
                        // Implemented via console later; keep a fact to skip
                        _queuedConsoleCommands.Add("ui_skip_intro");
                        break;
                    default:
                        ConsoleSystem.Log($"[CLI] Unknown flag: -{flag}", ConsoleChannel.System);
                        break;
                }

                continue;
            }
        }

        // Apply once settings are known; dispatch SettingsAppliedEvent triggers SettingsService
        if (settings.Count > 0)
            _pendingSettingsOverrides = settings;
    }

    private void ApplyResolutionPart(Godot.Collections.Dictionary<string, Variant> settings, int? width, int? height)
    {
        var current = settings.ContainsKey("display/resolution")
            ? (string)settings["display/resolution"]
            : null;

        int curW = 0, curH = 0;
        if (!string.IsNullOrEmpty(current))
        {
            var parts = current.Split('x');
            if (parts.Length == 2)
            {
                int.TryParse(parts[0], out curW);
                int.TryParse(parts[1], out curH);
            }
        }

        var newW = width ?? (curW > 0 ? curW : 1920);
        var newH = height ?? (curH > 0 ? curH : 1080);
        settings["display/resolution"] = $"{newW}x{newH}";
    }

    public void OnGameEvent(GameInitializedEvent eventArgs)
    {
        // Apply settings overrides after defaults have been loaded/applied by SettingsService
        if (_pendingSettingsOverrides != null && _pendingSettingsOverrides.Count > 0)
        {
            GameEvent.DispatchGlobal(new SettingsAppliedEvent(_pendingSettingsOverrides));
            ConsoleSystem.Log("[CLI] Applied temporary video overrides", ConsoleChannel.System);
            _pendingSettingsOverrides = null;
        }
    }

    public void OnGameEvent(PlayerSpawnedEvent eventArgs)
    {
        // Fallback: if world init not reached, schedule after spawn
        ScheduleExecutionWhenConsoleStable();
    }

    public void OnGameEvent(ConsoleCommandRegisteredEvent eventArgs)
    {
        // Track last time a command was registered to wait for a quiet window
        _lastConsoleRegistrationMs = Time.GetTicksMsec();
    }

    private void ScheduleExecutionWhenConsoleStable()
    {
        if (_executionScheduled) return;
        if (_queuedConsoleCommands.Count == 0) return;
        _executionScheduled = true;
        _ = WaitForConsoleQuietThenExecuteAsync();
    }

    private async Task WaitForConsoleQuietThenExecuteAsync()
    {
        // Wait for registrations to settle to ensure all systems have registered their commands
        var quietWindowMs = 300.0; // no registrations for this long
        var maxWaitMs = 5000.0; // cap waiting in case of constant activity
        var startMs = Time.GetTicksMsec();

        // Initialize last seen time to now if nothing registered yet
        if (_lastConsoleRegistrationMs <= 0) _lastConsoleRegistrationMs = startMs;

        while (true)
        {
            var now = Time.GetTicksMsec();
            var sinceLastReg = now - _lastConsoleRegistrationMs;
            var sinceStart = now - startMs;
            if (sinceLastReg >= quietWindowMs || sinceStart >= maxWaitMs) break;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        await ExecuteQueuedCommandsAsync();
    }

    private async Task ExecuteQueuedCommandsAsync()
    {
        if (_queuedConsoleCommands.Count == 0) return;

        // Wait one idle frame to ensure consoles and systems are ready
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        foreach (var cmd in _queuedConsoleCommands.ToList())
        {
            ConsoleSystem.Log($"[CLI] Executing: {cmd}", ConsoleChannel.Input);
            try
            {
                await ConsoleSystem.Instance.ExecuteCommand(cmd);
            }
            catch (Exception ex)
            {
                ConsoleSystem.Log($"[CLI] Error executing '{cmd}': {ex.Message}", ConsoleChannel.Error);
            }
        }

        _queuedConsoleCommands.Clear();
    }
}