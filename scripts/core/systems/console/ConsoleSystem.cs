using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Waterjam.Events;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FileAccess = Godot.FileAccess;

#pragma warning disable CS1998

namespace Waterjam.Core.Systems.Console;

public partial class ConsoleSystem : Node,
    IGameEventHandler<ConsoleCommandRegisteredEvent>,
    IGameEventHandler<GameInitializedEvent>
{
    private static ConsoleSystem instance;
    private readonly Dictionary<string, ConsoleCommand> commands = new();
    private readonly List<ConsoleMessage> messageHistory = new();
    private const int MaxHistorySize = 1000;
    private static readonly HashSet<ConsoleChannel> _disabledChannels = new();
    private StreamWriter logFile;
    private string logFilePath;
    private readonly List<string> _cliQueuedCommands = new();
    private Godot.Collections.Dictionary<string, Variant> _cliPendingSettingsOverrides;
    private bool _cliParsed;
    private string _mcpInboxDir;
    private Timer _mcpInboxTimer;
    private int _mainThreadId;

    public static ConsoleSystem Instance => instance;

    public override void _Ready()
    {
        instance = this;
        _mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        RegisterBaseCommands();
        InitializeLogging();
        TryParseCommandLine();
        InitializeMcpInbox();
    }

    public bool IsOnMainThread => System.Threading.Thread.CurrentThread.ManagedThreadId == _mainThreadId;

    private void RegisterBaseCommands()
    {
        RegisterCommand(new ConsoleCommand(
            "help",
            "Lists all available commands",
            "help [command]",
            async (args) =>
            {
                if (args.Length > 0)
                {
                    var cmdName = args[0].ToLower();
                    if (commands.TryGetValue(cmdName, out var cmd))
                    {
                        Log($"Command: {cmd.Name}", ConsoleChannel.System);
                        Log($"Description: {cmd.Description}", ConsoleChannel.System);
                        Log($"Usage: {cmd.Usage}", ConsoleChannel.System);
                        return true;
                    }

                    Log($"Command '{cmdName}' not found", ConsoleChannel.Error);
                    return false;
                }

                Log("Available commands:", ConsoleChannel.System);
                foreach (var cmd in commands.Values.OrderBy(c => c.Name))
                    Log($"  {cmd.Name} - {cmd.Description}", ConsoleChannel.System);

                return true;
            }
        ));

        // Logging channel control: log_channel <channel> <on|off>
        RegisterCommand(new ConsoleCommand(
            "log_channel",
            "Enable or disable a console log channel",
            "log_channel <channel> <on|off>",
            async args =>
            {
                if (args.Length < 2)
                {
                    Log("Usage: log_channel <channel> <on|off>", ConsoleChannel.Error);
                    return false;
                }

                var name = args[0];
                var on = args[1].ToLower() == "on";
                if (!Enum.TryParse<ConsoleChannel>(name, true, out var channel))
                {
                    Log($"Unknown channel: {name}", ConsoleChannel.Error);
                    return false;
                }

                if (on) EnableChannel(channel);
                else DisableChannel(channel);
                Log($"Channel {channel} {(on ? "enabled" : "disabled")}", ConsoleChannel.System);
                return true;
            }
        ));

        // List channel states
        RegisterCommand(new ConsoleCommand(
            "log_channels",
            "List all console log channels and whether they are enabled",
            "log_channels",
            async _ =>
            {
                foreach (ConsoleChannel ch in Enum.GetValues(typeof(ConsoleChannel)))
                {
                    var enabled = IsChannelEnabled(ch) ? "on" : "off";
                    Log($"{ch}: {enabled}", ConsoleChannel.System);
                }

                return true;
            }
        ));

        RegisterCommand(new ConsoleCommand(
            "clear",
            "Clears the console",
            "clear",
            async (_) =>
            {
                messageHistory.Clear();
                GameEvent.DispatchGlobal(new ConsoleHistoryClearedEvent());
                return true;
            }
        ));

        RegisterCommand(new ConsoleCommand(
            "quit",
            "Quits the game",
            "quit",
            async (_) =>
            {
                GameEvent.DispatchGlobal(new QuitRequestedEvent());
                return true;
            }
        ));

        RegisterCommand(new ConsoleCommand(
            "exit",
            "Alias for quit",
            "exit",
            async (args) => await ExecuteCommand("quit")
        ));

        RegisterCommand(new ConsoleCommand(
            "hud_variant",
            "Switch HUD variant at runtime (classic|v2)",
            "hud_variant <classic|v2>",
            async (args) =>
            {
                if (args.Length < 1)
                {
                    Log("Usage: hud_variant <classic|v2>", ConsoleChannel.Error);
                    return false;
                }

                var variant = args[0].ToLower();
                if (variant != "classic" && variant != "v2")
                {
                    Log("Invalid variant. Use 'classic' or 'v2'", ConsoleChannel.Error);
                    return false;
                }

                var settings = new Godot.Collections.Dictionary<string, Variant>
                {
                    { "ui/hud_variant", variant }
                };
                GameEvent.DispatchGlobal(new SettingsAppliedEvent(settings));
                Log($"HUD variant set to {variant}", ConsoleChannel.UI);
                return true;
            }
        ));

        // Quick-load dev citygen map
        RegisterCommand(new ConsoleCommand(
            "load_dev",
            "Loads the dev city generation sandbox scene",
            "load_dev",
            async _ =>
            {
                GameEvent.DispatchGlobal(new SceneLoadRequestedEvent("res://scenes/dev/dev.tscn"));
                return true;
            }
        ));

        // Map loading command: `map <scene_path>` e.g., map res://scenes/dev/dev.tscn
        RegisterCommand(new ConsoleCommand(
            "map",
            "Loads a scene by path. Usage: map <res://path/to/scene.tscn> (accepts /dev/*.tscn)",
            "map res://scenes/dev/dev.tscn",
            async args =>
            {
                if (args.Length < 1)
                {
                    Log("Usage: map <res://path/to/scene.tscn>", ConsoleChannel.Error);
                    return false;
                }

                var scenePath = (args[0] ?? string.Empty).Trim();

                // Normalize shorthand like /dev/dev.tscn to res://scenes/dev/dev.tscn
                if (scenePath.StartsWith("/"))
                    scenePath = "res://scenes" + scenePath;

                // If no scheme provided, assume res://
                if (!scenePath.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
                    scenePath = "res://" + scenePath.TrimStart('/');

                // Ensure .tscn extension
                if (!scenePath.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase))
                    scenePath += ".tscn";

                // Validate file exists
                try
                {
                    if (!FileAccess.FileExists(scenePath))
                    {
                        Log($"Scene not found: {scenePath}", ConsoleChannel.Error);
                        return false;
                    }
                }
                catch { /* best-effort */ }

                GameEvent.DispatchGlobal(new SceneLoadRequestedEvent(scenePath));
                return true;
            }
        ));


        // Alias for test harness to match request: `run_test_harness` -> `run_tests`
        RegisterCommand(new ConsoleCommand(
            "run_test_harness",
            "Alias of run_tests to execute the Godot test harness",
            "run_test_harness",
            async args => { return await ExecuteCommand("run_tests"); }
        ));

        // Legacy scene access commands for development
        RegisterCommand(new ConsoleCommand(
            "load_dev_legacy",
            "Load the legacy dev scene for testing",
            "load_dev_legacy",
            async args =>
            {
                GameEvent.DispatchGlobal(new SceneLoadRequestedEvent("res://scenes/legacy/dev.tscn"));
                Log("Loading legacy dev scene", ConsoleChannel.System);
                return true;
            }
        ));

        RegisterCommand(new ConsoleCommand(
            "load_dev_city_legacy",
            "Load the legacy dev_city scene for testing",
            "load_dev_city_legacy",
            async args =>
            {
                GameEvent.DispatchGlobal(new SceneLoadRequestedEvent("res://scenes/legacy/dev_city.tscn"));
                Log("Loading legacy dev_city scene", ConsoleChannel.System);
                return true;
            }
        ));
    }

    public void RegisterCommand(ConsoleCommand command)
    {
        commands[command.Name.ToLower()] = command;
        GameEvent.DispatchGlobal(new ConsoleCommandRegisteredEvent(command));
    }

    public bool UnregisterCommand(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        var key = name.ToLower();
        if (commands.Remove(key, out var removed))
        {
            GameEvent.DispatchGlobal(new ConsoleCommandUnregisteredEvent(removed));
            Log($"Unregistered command: {removed.Name}", ConsoleChannel.System);
            return true;
        }

        LogWarn($"Attempted to unregister unknown command: {name}");
        return false;
    }

    public async Task<bool> ExecuteCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var parts = input.Split(' ');
        var cmdName = parts[0].ToLower();
        var args = parts.Skip(1).ToArray();

        if (commands.TryGetValue(cmdName, out var command))
            try
            {
                Log($"> {input}", ConsoleChannel.Input);
                return await command.Execute(args);
            }
            catch (Exception ex)
            {
                Log($"Error executing command: {ex.Message}", ConsoleChannel.Error);
                return false;
            }

        Log($"Unknown command: {cmdName}", ConsoleChannel.Error);
        return false;
    }

    private void InitializeLogging()
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var logDir = Path.Combine(OS.GetUserDataDir(), "logs");

            // Ensure log directory exists
            if (!Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);

            logFilePath = Path.Combine(logDir, $"game_log_{timestamp}.txt");
            logFile = new StreamWriter(logFilePath, true, Encoding.UTF8)
            {
                AutoFlush = true // Ensure logs are written immediately
            };

            // Write header
            logFile.WriteLine($"=== Game Log Started at {timestamp} ===\n");
        }
        catch (Exception ex)
        {
            // Don't use LogErr during initialization to avoid recursion
            // Just output directly to Godot console and system console
            GD.PushError($"[ConsoleSystem] Failed to initialize logging: {ex.Message}");
            System.Console.Error.WriteLine($"[ConsoleSystem] Failed to initialize logging: {ex.Message}");
        }
    }

    private void TryParseCommandLine()
    {
        if (_cliParsed) return;
        _cliParsed = true;

        string[] args;
        try
        {
            args = OS.GetCmdlineUserArgs();
        }
        catch
        {
            args = OS.GetCmdlineArgs();
        }

        if (args == null || args.Length == 0) return;

        Log($"[CLI] Args: {string.Join(' ', args)}", ConsoleChannel.System);

        var settings = new Godot.Collections.Dictionary<string, Variant>();
        string awaitingValueFor = null;

        for (int i = 0; i < args.Length; i++)
        {
            var token = args[i].Trim();
            if (string.IsNullOrEmpty(token)) continue;

            if (awaitingValueFor != null)
            {
                if (awaitingValueFor == "w" && int.TryParse(token, out var w)) ApplyResolutionPart(settings, w, null);
                else if (awaitingValueFor == "h" && int.TryParse(token, out var h)) ApplyResolutionPart(settings, null, h);
                awaitingValueFor = null;
                continue;
            }

            if (token.StartsWith("+"))
            {
                var command = token.Substring(1);
                var collected = new List<string> { command };
                int j = i + 1;
                while (j < args.Length && !string.IsNullOrWhiteSpace(args[j]) && args[j][0] != '-' && args[j][0] != '+')
                {
                    collected.Add(args[j]);
                    j++;
                }

                i = j - 1;
                _cliQueuedCommands.Add(string.Join(' ', collected));
                continue;
            }

            if (token.StartsWith("-"))
            {
                var flag = token.TrimStart('-').ToLowerInvariant();
                switch (flag)
                {
                    case "windowed":
                    case "w1": settings["display/display_mode"] = 0; break;
                    case "fullscreen":
                    case "fs": settings["display/display_mode"] = 1; break;
                    case "exclusive": settings["display/display_mode"] = 2; break;
                    case "w": awaitingValueFor = "w"; break;
                    case "h": awaitingValueFor = "h"; break;
                    case "vsync": settings["display/vsync"] = true; break;
                    case "novsync": settings["display/vsync"] = false; break;
                    case "skipintro": _cliQueuedCommands.Add("ui_skip_intro"); break;
                    default: Log($"[CLI] Unknown flag: -{flag}", ConsoleChannel.System); break;
                }
            }
        }

        if (settings.Count > 0) _cliPendingSettingsOverrides = settings;
    }

    private void InitializeMcpInbox()
    {
        try
        {
            var userDir = OS.GetUserDataDir();
            var mcpDir = Path.Combine(userDir, "mcp");
            _mcpInboxDir = Path.Combine(mcpDir, "inbox");
            if (!Directory.Exists(_mcpInboxDir))
                Directory.CreateDirectory(_mcpInboxDir);

            _mcpInboxTimer = new Timer
            {
                Autostart = true,
                OneShot = false,
                WaitTime = 0.5,
                ProcessCallback = Timer.TimerProcessCallback.Idle
            };
            // Defer add_child to avoid parent busy errors during _Ready
            CallDeferred(Node.MethodName.AddChild, _mcpInboxTimer);
            _mcpInboxTimer.Timeout += OnMcpInboxTimerTimeout;
            Log("[MCP] Inbox watcher initialized", ConsoleChannel.System);
        }
        catch (Exception ex)
        {
            LogWarn($"[MCP] Failed to initialize inbox: {ex.Message}");
        }
    }

    private async void OnMcpInboxTimerTimeout()
    {
        if (string.IsNullOrEmpty(_mcpInboxDir)) return;
        string[] files;
        try
        {
            files = Directory.GetFiles(_mcpInboxDir);
        }
        catch
        {
            return;
        }

        if (files == null || files.Length == 0) return;

        foreach (var file in files)
        {
            string workingPath = null;
            try
            {
                // Move to a temporary working name to avoid races with writers
                workingPath = file + ".working";
                try
                {
                    File.Move(file, workingPath);
                }
                catch
                {
                    continue;
                }

                string text;
                try
                {
                    text = File.ReadAllText(workingPath, Encoding.UTF8);
                }
                catch
                {
                    continue;
                }

                var cmd = (text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(cmd))
                {
                    LogWarn($"[MCP] Ignoring empty command file: {Path.GetFileName(file)}");
                }
                else
                {
                    Log($"[MCP] Executing command from inbox: {cmd}", ConsoleChannel.Input);
                    try
                    {
                        var ok = await ExecuteCommand(cmd);
                        TryWriteMcpOutboxResult(cmd, ok, null);
                    }
                    catch (Exception ex)
                    {
                        Log($"[MCP] Error executing inbox command '{cmd}': {ex.Message}", ConsoleChannel.Error);
                        TryWriteMcpOutboxResult(cmd, false, ex.Message);
                    }
                }
            }
            finally
            {
                try
                {
                    if (workingPath != null && File.Exists(workingPath)) File.Delete(workingPath);
                }
                catch
                {
                    /* best-effort cleanup */
                }
            }
        }
    }

    private void TryWriteMcpOutboxResult(string command, bool success, string error)
    {
        try
        {
            var userDir = OS.GetUserDataDir();
            var mcpDir = System.IO.Path.Combine(userDir, "mcp");
            var outbox = System.IO.Path.Combine(mcpDir, "outbox");
            if (!System.IO.Directory.Exists(outbox)) System.IO.Directory.CreateDirectory(outbox);

            var fileNameSafe = command.Replace(' ', '_').Replace('/', '_').Replace('\\', '_');
            var timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var path = System.IO.Path.Combine(outbox, $"{timestamp}_{fileNameSafe}.txt");
            var status = success ? "OK" : "ERROR";
            var content = $"status={status}\ncommand={command}\n" + (string.IsNullOrEmpty(error) ? string.Empty : $"error={error}\n");
            System.IO.File.WriteAllText(path, content, System.Text.Encoding.UTF8);
        }
        catch
        {
            // best-effort
        }
    }

    private void ApplyResolutionPart(Godot.Collections.Dictionary<string, Variant> settings, int? width, int? height)
    {
        var current = settings.ContainsKey("display/resolution") ? (string)settings["display/resolution"] : null;
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
        if (_cliPendingSettingsOverrides != null && _cliPendingSettingsOverrides.Count > 0)
        {
            GameEvent.DispatchGlobal(new SettingsAppliedEvent(_cliPendingSettingsOverrides));
            Log("[CLI] Applied temporary video overrides", ConsoleChannel.System);
            _cliPendingSettingsOverrides = null;
        }

        // Execute any queued CLI commands early so commands like run_tests work from main menu
        _ = ExecuteQueuedCliCommandsAsync();
    }

    private async Task ExecuteQueuedCliCommandsAsync()
    {
        if (_cliQueuedCommands.Count == 0) return;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        foreach (var cmd in _cliQueuedCommands.ToList())
        {
            Log($"[CLI] Executing: {cmd}", ConsoleChannel.Input);
            try
            {
                await ExecuteCommand(cmd);
            }
            catch (Exception ex)
            {
                Log($"[CLI] Error executing '{cmd}': {ex.Message}", ConsoleChannel.Error);
            }
        }

        _cliQueuedCommands.Clear();
    }

    public static void Log(string message, ConsoleChannel channel = ConsoleChannel.Info)
    {
        // Respect channel filtering (errors always log)
        try
        {
            if (channel != ConsoleChannel.Error && _disabledChannels.Contains(channel))
            {
                return;
            }
        }
        catch
        {
            /* guard against static init races */
        }

        var safeText = message ?? "(null)";
        ConsoleMessage consoleMessage = null;

        try
        {
            consoleMessage = new ConsoleMessage(safeText, channel, DateTime.Now);
        }
        catch (Exception)
        {
            // If ConsoleMessage creation fails, use fallback values
            consoleMessage = null;
        }

        // Always print to Godot output/STDOUT so early logs are visible even before initialization
        try
        {
            string formatted = null;

            try
            {
                if (consoleMessage != null)
                {
                    var timestamp = consoleMessage.Timestamp.ToString("HH:mm:ss");
                    var channelStr = consoleMessage.Channel.ToString();
                    var text = consoleMessage.Text ?? "(null)";
                    formatted = "[" + timestamp + "] [" + channelStr + "] " + text;
                }
                else
                {
                    var timestamp = DateTime.Now.ToString("HH:mm:ss");
                    var channelStr = channel.ToString();
                    formatted = "[" + timestamp + "] [" + channelStr + "] " + safeText;
                }
            }
            catch (Exception)
            {
                // Fallback to simple formatting if string interpolation fails
                try
                {
                    var timestamp = DateTime.Now.ToString("HH:mm:ss");
                    var channelStr = channel.ToString();
                    formatted = "[" + timestamp + "] [" + channelStr + "] " + safeText;
                }
                catch (Exception)
                {
                    // Absolute fallback - no string interpolation
                    formatted = "[" + DateTime.Now.ToString("HH:mm:ss") + "] [" + channel.ToString() + "] " + safeText;
                }
            }

            if (string.IsNullOrEmpty(formatted))
            {
                formatted = "[" + DateTime.Now.ToString("HH:mm:ss") + "] [" + channel.ToString() + "] " + safeText;
            }

            // Always write to System.Console first for reliability
            switch (channel)
            {
                case ConsoleChannel.Error:
                    System.Console.Error.WriteLine(formatted ?? safeText);
                    break;
                case ConsoleChannel.Warning:
                    System.Console.WriteLine(formatted ?? safeText);
                    break;
                default:
                    System.Console.WriteLine(formatted ?? safeText);
                    break;
            }

            // Try to write to Godot output if available (but don't fail if it's not)
            // Only push to Godot's error/warning system for explicit Godot channels
            try
            {
                switch (channel)
                {
                    case ConsoleChannel.GodotError:
                        if (!string.IsNullOrEmpty(formatted)) GD.PushError(formatted);
                        break;
                    case ConsoleChannel.GodotWarning:
                        if (!string.IsNullOrEmpty(formatted)) GD.PushWarning(formatted);
                        break;
                    default:
                        if (!string.IsNullOrEmpty(formatted)) GD.Print(formatted);
                        break;
                }
            }
            catch (Exception)
            {
                /* GD calls failed - that's OK, we already wrote to System.Console */
            }
        }
        catch
        {
            // Fallback to absolute minimal logging if everything else fails
            try
            {
                System.Console.Error.WriteLine("[CONSOLE ERROR] Failed to log message: " + (message ?? "(null)"));
            }
            catch
            {
                /* give up */
            }
        }

        // If the system is initialized, also store history, write to file
        // Only dispatch events or touch Godot objects on the main thread
        if (Instance != null)
        {
            try
            {
                Instance.messageHistory.Add(consoleMessage);
            }
            catch
            {
                /* history not ready */
            }

            try
            {
                while (Instance.messageHistory.Count > MaxHistorySize)
                    Instance.messageHistory.RemoveAt(0);
            }
            catch
            {
                /* history resize not ready */
            }

            try
            {
                Instance.WriteToLog(consoleMessage);
            }
            catch
            {
                /* log file not ready yet */
            }

            try
            {
                if (Instance.IsOnMainThread)
                {
                    GameEvent.DispatchGlobal(new ConsoleMessageLoggedEvent(consoleMessage));
                }
                // Else: skip dispatch from background threads to avoid cross-thread errors
            }
            catch
            {
                /* bus not ready */
            }
        }
    }

    private void WriteToLog(ConsoleMessage message)
    {
        try
        {
            if (logFile == null) return;
            var formattedMessage = $"[{message.Timestamp:HH:mm:ss.fff}] [{message.Channel}] {message.Text}";
            logFile.WriteLine(formattedMessage);
        }
        catch (Exception ex)
        {
            // Don't use LogErr here to avoid infinite recursion
            // Just output directly to Godot console and system console
            GD.PushError($"[ConsoleSystem] Failed to write to log file: {ex.Message}");
            System.Console.Error.WriteLine($"[ConsoleSystem] Failed to write to log file: {ex.Message}");
        }
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        logFile?.Dispose();
    }

    public static void LogErr(string message, ConsoleChannel channel = ConsoleChannel.Error)
    {
        Log(message, channel);
    }

    public static void LogWarn(string message, ConsoleChannel channel = ConsoleChannel.Warning)
    {
        Log(message, channel);
    }

    /// <summary>
    /// Log an error message and push it to Godot's error system
    /// </summary>
    public static void LogGodotError(string message)
    {
        Log(message, ConsoleChannel.GodotError);
    }

    /// <summary>
    /// Log a warning message and push it to Godot's warning system
    /// </summary>
    public static void LogGodotWarning(string message)
    {
        Log(message, ConsoleChannel.GodotWarning);
    }

    public IEnumerable<ConsoleMessage> GetHistory()
    {
        return messageHistory;
    }

    public void OnGameEvent(ConsoleCommandRegisteredEvent eventArgs)
    {
        Log($"Registered command: {eventArgs.Command.Name}", ConsoleChannel.System);
        // If any queued CLI command targets this name, try executing now
        if (_cliQueuedCommands.Count == 0) return;
        var name = eventArgs.Command.Name.ToLower();
        var matching = _cliQueuedCommands.Where(c =>
        {
            var p = c.Split(' ');
            return p.Length > 0 && p[0].Equals(name, StringComparison.OrdinalIgnoreCase);
        }).ToList();
        if (matching.Count == 0) return;
        _ = ExecuteQueuedCliCommandsAsync();
    }
}

// Channel filtering helpers
public partial class ConsoleSystem
{
    public static void DisableChannel(ConsoleChannel channel)
    {
        if (channel == ConsoleChannel.Error) return; // Never disable errors
        _disabledChannels.Add(channel);
    }

    public static void EnableChannel(ConsoleChannel channel)
    {
        _disabledChannels.Remove(channel);
    }

    public static bool IsChannelEnabled(ConsoleChannel channel)
    {
        if (channel == ConsoleChannel.Error) return true;
        return !_disabledChannels.Contains(channel);
    }
}