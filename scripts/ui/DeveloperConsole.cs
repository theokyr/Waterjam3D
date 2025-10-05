using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Waterjam.Core.Systems.Console;
using Waterjam.Events;
using System.Threading.Tasks;

#pragma warning disable CS4014

namespace Waterjam.UI;

public partial class DeveloperConsole : Control,
    IGameEventHandler<ConsoleMessageLoggedEvent>,
    IGameEventHandler<ConsoleCommandRegisteredEvent>,
    IGameEventHandler<ConsoleHistoryClearedEvent>
{
    private RichTextLabel outputText;
    private LineEdit inputField;
    private Button copyButton;
    private Dictionary<ConsoleChannel, bool> channelFilters = new();
    private List<string> commandHistory = new();
    private int historyIndex = -1;
    private const int MaxCommandHistory = 50;
    private GridContainer channelToggles;
    private bool isResizing;
    private float minHeight = 200;
    private float maxHeight = 800;
    private OptionButton questSelector;
    private Button startQuestButton;
    private Button completeQuestButton;

    public override void _Ready()
    {
        // Get existing controls
        outputText = GetNode<RichTextLabel>("%OutputText");
        inputField = GetNode<LineEdit>("%InputField");
        channelToggles = GetNode<GridContainer>("%ChannelToggles");
        copyButton = GetNode<Button>("%CopyButton");
        questSelector = GetNode<OptionButton>("%QuestSelector");
        startQuestButton = GetNode<Button>("Panel/MarginContainer/VBoxContainer/TopContainer/QuestControls/StartQuest");
        completeQuestButton = GetNode<Button>("Panel/MarginContainer/VBoxContainer/TopContainer/QuestControls/CompleteQuest");

        // Connect signals
        copyButton.Pressed += OnCopyButtonPressed;

        // Initialize channel toggles
        InitializeChannelToggles();

        // Load existing history
        foreach (var message in ConsoleSystem.Instance.GetHistory())
            AppendMessage(message);

        Hide();

        // Setup resize handle
        var resizeHandle = GetNode<Control>("Panel/Resize");
        resizeHandle.GuiInput += OnResizeHandleInput;
    }

    private void InitializeChannelToggles()
    {
        // Clear any existing toggles
        foreach (var child in channelToggles.GetChildren())
            child.QueueFree();

        // Create toggle for each channel
        foreach (ConsoleChannel channel in Enum.GetValues(typeof(ConsoleChannel)))
        {
            var toggle = new CheckBox
            {
                Text = channel.ToString(),
                CustomMinimumSize = new Vector2(120, 0),
                ButtonPressed = true
            };

            toggle.AddThemeColorOverride("font_color", GetChannelColor(channel));
            toggle.AddThemeConstantOverride("h_separation", 8);

            channelFilters[channel] = true;
            toggle.Toggled += (bool pressed) => OnChannelToggled(channel, pressed);
            channelToggles.AddChild(toggle);
        }
    }

    private void OnChannelToggled(ConsoleChannel channel, bool enabled)
    {
        channelFilters[channel] = enabled;
        RefreshOutput();
    }

    private void RefreshOutput()
    {
        outputText.Clear();
        foreach (var message in ConsoleSystem.Instance.GetHistory())
            AppendMessage(message);
    }

    public override void _Input(InputEvent @event)
    {
        if (Input.IsActionJustPressed("ui_toggle_developer_console"))
        {
            if (!Visible)
                Show();
            else
                Hide();
            GetViewport().SetInputAsHandled();
        }

        if (Visible && @event is InputEventKey eventKey && eventKey.Pressed)
            switch (eventKey.Keycode)
            {
                case Key.Up:
                    NavigateHistory(-1);
                    GetViewport().SetInputAsHandled();
                    break;

                case Key.Down:
                    NavigateHistory(1);
                    GetViewport().SetInputAsHandled();
                    break;

                case Key.Enter:
                    if (inputField.HasFocus())
                    {
                        ExecuteCommand();
                        GetViewport().SetInputAsHandled();
                    }

                    break;
            }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Visible)
            GetViewport().SetInputAsHandled();
    }

    private new void Show()
    {
        Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;

        inputField.GrabFocus();
    }

    private new void Hide()
    {
        Visible = false;
    }

    private void NavigateHistory(int direction)
    {
        if (commandHistory.Count == 0) return;

        historyIndex = Math.Clamp(historyIndex + direction, -1, commandHistory.Count - 1);

        if (historyIndex == -1)
            inputField.Text = "";
        else
            inputField.Text = commandHistory[historyIndex];

        inputField.CaretColumn = inputField.Text.Length;
    }

    private async Task ExecuteCommand()
    {
        var command = inputField.Text.Trim();
        if (string.IsNullOrEmpty(command)) return;

        // Add to history if different from last command
        if (commandHistory.Count == 0 || commandHistory.Last() != command)
        {
            commandHistory.Add(command);
            if (commandHistory.Count > MaxCommandHistory)
                commandHistory.RemoveAt(0);
        }

        historyIndex = -1;
        inputField.Text = "";

        await ConsoleSystem.Instance.ExecuteCommand(command);
    }

    private void AppendMessage(ConsoleMessage message)
    {
        bool isEnabled;
        if (!channelFilters.TryGetValue(message.Channel, out isEnabled) || !isEnabled)
            return;

        var timestamp = message.Timestamp.ToString("HH:mm:ss");
        var channelColor = GetChannelColor(message.Channel);

        outputText.PushColor(channelColor);
        outputText.AddText($"[{timestamp}][{message.Channel}] ");
        outputText.Pop(); // Pop color

        outputText.AddText($"{message.Text}\n");
        outputText.ScrollToLine(outputText.GetLineCount() - 1);
    }

    private Color GetChannelColor(ConsoleChannel channel)
    {
        return channel switch
        {
            ConsoleChannel.System => Colors.White,
            ConsoleChannel.Info => Colors.LightBlue,
            ConsoleChannel.Warning => Colors.Yellow,
            ConsoleChannel.Error => Colors.Red,
            ConsoleChannel.Debug => Colors.Gray,
            ConsoleChannel.Input => Colors.Green,
            ConsoleChannel.Quest => Colors.Orange,
            ConsoleChannel.Script => Colors.Purple,
            ConsoleChannel.UI => Colors.Pink,
            ConsoleChannel.Game => Colors.Blue,
            ConsoleChannel.Audio => Colors.YellowGreen,
            ConsoleChannel.Dialogue => Colors.Aquamarine,
            ConsoleChannel.Npc => Colors.MediumPurple,
            ConsoleChannel.Player => Colors.LightGreen,
            ConsoleChannel.World => Colors.DarkOrange,
            _ => Colors.White
        };
    }

    public void OnGameEvent(ConsoleMessageLoggedEvent eventArgs)
    {
        AppendMessage(eventArgs.Message);
    }

    public void OnGameEvent(ConsoleCommandRegisteredEvent eventArgs)
    {
        // Optional: Show command registration in console
    }

    public void OnGameEvent(ConsoleHistoryClearedEvent eventArgs)
    {
        outputText.Clear();
    }

    private void OnCopyButtonPressed()
    {
        var plainText = outputText.Text;
        if (DisplayServer.HasFeature(DisplayServer.Feature.Clipboard))
        {
            DisplayServer.ClipboardSet(plainText);
            ConsoleSystem.Log("Console output copied to clipboard", ConsoleChannel.UI);
        }
        else
        {
            ConsoleSystem.LogErr("Clipboard functionality not available on this platform", ConsoleChannel.UI);
        }
    }

    public override void _ExitTree()
    {
        base._ExitTree();
    }

    private void OnResizeHandleInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseEvent)
        {
            if (mouseEvent.ButtonIndex == MouseButton.Left) isResizing = mouseEvent.Pressed;
        }
        else if (@event is InputEventMouseMotion motionEvent && isResizing)
        {
            var panel = GetNode<Control>("Panel");
            var newHeight = panel.Size.Y + motionEvent.Relative.Y;
            newHeight = Mathf.Clamp(newHeight, minHeight, maxHeight);
            panel.CustomMinimumSize = new Vector2(0, newHeight);
        }
    }
}