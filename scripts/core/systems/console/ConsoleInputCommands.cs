using System;
using System.Globalization;
using System.Threading.Tasks;
using Godot;

namespace Waterjam.Core.Systems.Console;

/// <summary>
/// Console commands for injecting input events into the game.
/// Used by AI agents via MCP inbox to operate the game autonomously.
/// </summary>
public static class ConsoleInputCommands
{
    public static void RegisterAll(ConsoleSystem console)
    {
        RegisterKeyboardCommands(console);
        RegisterMouseCommands(console);
        RegisterControllerCommands(console);
        RegisterActionCommands(console);
    }

    private static void RegisterKeyboardCommands(ConsoleSystem console)
    {
        console.RegisterCommand(new ConsoleCommand(
            "input_key_press",
            "Simulate a key press event",
            "input_key_press <keycode> [physical:true|false]",
            async args =>
            {
                if (args.Length < 1)
                {
                    ConsoleSystem.Log("Usage: input_key_press <keycode> [physical:true|false]", ConsoleChannel.Error);
                    ConsoleSystem.Log("Example: input_key_press W physical:true", ConsoleChannel.Info);
                    return false;
                }

                var keyName = args[0].ToUpperInvariant();
                var physical = args.Length > 1 && args[1].ToLowerInvariant().Contains("true");

                if (!Enum.TryParse<Key>(keyName, true, out var key))
                {
                    ConsoleSystem.Log($"Invalid key: {keyName}", ConsoleChannel.Error);
                    return false;
                }

                var inputEvent = new InputEventKey
                {
                    Keycode = key,
                    Pressed = true,
                    PhysicalKeycode = physical ? key : Key.None,
                    Echo = false
                };

                Input.ParseInputEvent(inputEvent);
                ConsoleSystem.Log($"[Input] Key pressed: {keyName} (physical: {physical})", ConsoleChannel.Input);
                return true;
            }
        ));

        console.RegisterCommand(new ConsoleCommand(
            "input_key_release",
            "Simulate a key release event",
            "input_key_release <keycode> [physical:true|false]",
            async args =>
            {
                if (args.Length < 1)
                {
                    ConsoleSystem.Log("Usage: input_key_release <keycode> [physical:true|false]", ConsoleChannel.Error);
                    ConsoleSystem.Log("Example: input_key_release W physical:true", ConsoleChannel.Info);
                    return false;
                }

                var keyName = args[0].ToUpperInvariant();
                var physical = args.Length > 1 && args[1].ToLowerInvariant().Contains("true");

                if (!Enum.TryParse<Key>(keyName, true, out var key))
                {
                    ConsoleSystem.Log($"Invalid key: {keyName}", ConsoleChannel.Error);
                    return false;
                }

                var inputEvent = new InputEventKey
                {
                    Keycode = key,
                    Pressed = false,
                    PhysicalKeycode = physical ? key : Key.None,
                    Echo = false
                };

                Input.ParseInputEvent(inputEvent);
                ConsoleSystem.Log($"[Input] Key released: {keyName} (physical: {physical})", ConsoleChannel.Input);
                return true;
            }
        ));

        console.RegisterCommand(new ConsoleCommand(
            "input_key_tap",
            "Simulate a quick key press and release",
            "input_key_tap <keycode> [physical:true|false] [hold_ms:100]",
            async args =>
            {
                if (args.Length < 1)
                {
                    ConsoleSystem.Log("Usage: input_key_tap <keycode> [physical:true|false] [hold_ms:100]", ConsoleChannel.Error);
                    ConsoleSystem.Log("Example: input_key_tap Space physical:true hold_ms:50", ConsoleChannel.Info);
                    return false;
                }

                var keyName = args[0].ToUpperInvariant();
                var physical = false;
                var holdMs = 100;

                // Parse optional arguments
                for (int i = 1; i < args.Length; i++)
                {
                    if (args[i].ToLowerInvariant().Contains("physical:true"))
                        physical = true;
                    else if (args[i].ToLowerInvariant().StartsWith("hold_ms:"))
                    {
                        var val = args[i].Split(':')[1];
                        if (int.TryParse(val, out var ms))
                            holdMs = ms;
                    }
                }

                if (!Enum.TryParse<Key>(keyName, true, out var key))
                {
                    ConsoleSystem.Log($"Invalid key: {keyName}", ConsoleChannel.Error);
                    return false;
                }

                // Press
                var pressEvent = new InputEventKey
                {
                    Keycode = key,
                    Pressed = true,
                    PhysicalKeycode = physical ? key : Key.None,
                    Echo = false
                };
                Input.ParseInputEvent(pressEvent);

                // Hold
                await Task.Delay(holdMs);

                // Release
                var releaseEvent = new InputEventKey
                {
                    Keycode = key,
                    Pressed = false,
                    PhysicalKeycode = physical ? key : Key.None,
                    Echo = false
                };
                Input.ParseInputEvent(releaseEvent);

                ConsoleSystem.Log($"[Input] Key tapped: {keyName} (hold: {holdMs}ms)", ConsoleChannel.Input);
                return true;
            }
        ));
    }

    private static void RegisterMouseCommands(ConsoleSystem console)
    {
        console.RegisterCommand(new ConsoleCommand(
            "input_mouse_move",
            "Simulate mouse movement to screen coordinates",
            "input_mouse_move <x> <y> [relative:true|false]",
            async args =>
            {
                if (args.Length < 2)
                {
                    ConsoleSystem.Log("Usage: input_mouse_move <x> <y> [relative:true|false]", ConsoleChannel.Error);
                    ConsoleSystem.Log("Example: input_mouse_move 640 360 or input_mouse_move 10 20 relative:true", ConsoleChannel.Info);
                    return false;
                }

                if (!float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                    !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                {
                    ConsoleSystem.Log("Invalid coordinates", ConsoleChannel.Error);
                    return false;
                }

                var relative = args.Length > 2 && args[2].ToLowerInvariant().Contains("true");

                if (relative)
                {
                    var inputEvent = new InputEventMouseMotion
                    {
                        Relative = new Vector2(x, y),
                        Velocity = new Vector2(x * 10, y * 10) // Approximate velocity
                    };
                    Input.ParseInputEvent(inputEvent);
                    ConsoleSystem.Log($"[Input] Mouse moved relatively: ({x}, {y})", ConsoleChannel.Input);
                }
                else
                {
                    var inputEvent = new InputEventMouseMotion
                    {
                        Position = new Vector2(x, y),
                        GlobalPosition = new Vector2(x, y)
                    };
                    Input.ParseInputEvent(inputEvent);
                    ConsoleSystem.Log($"[Input] Mouse moved to: ({x}, {y})", ConsoleChannel.Input);
                }

                return true;
            }
        ));

        console.RegisterCommand(new ConsoleCommand(
            "input_mouse_button",
            "Simulate mouse button press or release",
            "input_mouse_button <button_index> <press|release> [x] [y]",
            async args =>
            {
                if (args.Length < 2)
                {
                    ConsoleSystem.Log("Usage: input_mouse_button <button_index> <press|release> [x] [y]", ConsoleChannel.Error);
                    ConsoleSystem.Log("Button indices: 1=Left, 2=Right, 3=Middle, 4=WheelUp, 5=WheelDown", ConsoleChannel.Info);
                    return false;
                }

                if (!int.TryParse(args[0], out var buttonIdx))
                {
                    ConsoleSystem.Log("Invalid button index", ConsoleChannel.Error);
                    return false;
                }

                var pressed = args[1].ToLowerInvariant() == "press";
                var buttonIndex = (MouseButton)(buttonIdx - 1); // Convert 1-based to 0-based

                // Optional position - default to center of screen
                var viewport = Engine.GetMainLoop() as SceneTree;
                Vector2 position = viewport?.Root?.GetViewport()?.GetMousePosition() ?? new Vector2(0, 0);
                if (args.Length >= 4)
                {
                    if (float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                        float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                    {
                        position = new Vector2(x, y);
                    }
                }

                var inputEvent = new InputEventMouseButton
                {
                    ButtonIndex = buttonIndex,
                    Pressed = pressed,
                    Position = position,
                    GlobalPosition = position
                };

                Input.ParseInputEvent(inputEvent);
                ConsoleSystem.Log($"[Input] Mouse button {buttonIndex} {(pressed ? "pressed" : "released")} at ({position.X}, {position.Y})", ConsoleChannel.Input);
                return true;
            }
        ));

        console.RegisterCommand(new ConsoleCommand(
            "input_mouse_click",
            "Simulate a complete mouse click (press + release)",
            "input_mouse_click <button_index> [x] [y] [hold_ms:100]",
            async args =>
            {
                if (args.Length < 1)
                {
                    ConsoleSystem.Log("Usage: input_mouse_click <button_index> [x] [y] [hold_ms:100]", ConsoleChannel.Error);
                    ConsoleSystem.Log("Button indices: 1=Left, 2=Right, 3=Middle", ConsoleChannel.Info);
                    return false;
                }

                if (!int.TryParse(args[0], out var buttonIdx))
                {
                    ConsoleSystem.Log("Invalid button index", ConsoleChannel.Error);
                    return false;
                }

                var buttonIndex = (MouseButton)(buttonIdx - 1);
                var viewport = Engine.GetMainLoop() as SceneTree;
                var position = viewport?.Root?.GetViewport()?.GetMousePosition() ?? new Vector2(0, 0);
                var holdMs = 100;

                // Parse optional position and hold time
                if (args.Length >= 3)
                {
                    if (float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                        float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                    {
                        position = new Vector2(x, y);
                    }
                }

                for (int i = 1; i < args.Length; i++)
                {
                    if (args[i].ToLowerInvariant().StartsWith("hold_ms:"))
                    {
                        var val = args[i].Split(':')[1];
                        if (int.TryParse(val, out var ms))
                            holdMs = ms;
                    }
                }

                // Press
                var pressEvent = new InputEventMouseButton
                {
                    ButtonIndex = buttonIndex,
                    Pressed = true,
                    Position = position,
                    GlobalPosition = position
                };
                Input.ParseInputEvent(pressEvent);

                // Hold
                await Task.Delay(holdMs);

                // Release
                var releaseEvent = new InputEventMouseButton
                {
                    ButtonIndex = buttonIndex,
                    Pressed = false,
                    Position = position,
                    GlobalPosition = position
                };
                Input.ParseInputEvent(releaseEvent);

                ConsoleSystem.Log($"[Input] Mouse clicked: button {buttonIndex} at ({position.X}, {position.Y})", ConsoleChannel.Input);
                return true;
            }
        ));

        console.RegisterCommand(new ConsoleCommand(
            "input_mouse_wheel",
            "Simulate mouse wheel scrolling",
            "input_mouse_wheel <delta> [x] [y]",
            async args =>
            {
                if (args.Length < 1)
                {
                    ConsoleSystem.Log("Usage: input_mouse_wheel <delta> [x] [y]", ConsoleChannel.Error);
                    ConsoleSystem.Log("Positive delta scrolls up, negative scrolls down", ConsoleChannel.Info);
                    return false;
                }

                if (!float.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var delta))
                {
                    ConsoleSystem.Log("Invalid delta value", ConsoleChannel.Error);
                    return false;
                }

                var viewport = Engine.GetMainLoop() as SceneTree;
                var position = viewport?.Root?.GetViewport()?.GetMousePosition() ?? new Vector2(0, 0);
                if (args.Length >= 3)
                {
                    if (float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                        float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                    {
                        position = new Vector2(x, y);
                    }
                }

                var buttonIndex = delta > 0 ? MouseButton.WheelUp : MouseButton.WheelDown;
                var inputEvent = new InputEventMouseButton
                {
                    ButtonIndex = buttonIndex,
                    Pressed = true,
                    Position = position,
                    GlobalPosition = position,
                    Factor = Math.Abs(delta)
                };

                Input.ParseInputEvent(inputEvent);
                ConsoleSystem.Log($"[Input] Mouse wheel: {delta} at ({position.X}, {position.Y})", ConsoleChannel.Input);
                return true;
            }
        ));
    }

    private static void RegisterControllerCommands(ConsoleSystem console)
    {
        console.RegisterCommand(new ConsoleCommand(
            "input_joy_button",
            "Simulate controller button press or release",
            "input_joy_button <device> <button_index> <press|release>",
            async args =>
            {
                if (args.Length < 3)
                {
                    ConsoleSystem.Log("Usage: input_joy_button <device> <button_index> <press|release>", ConsoleChannel.Error);
                    ConsoleSystem.Log("Common buttons: 0=A/Cross, 1=B/Circle, 2=X/Square, 3=Y/Triangle", ConsoleChannel.Info);
                    return false;
                }

                if (!int.TryParse(args[0], out var device) || !int.TryParse(args[1], out var buttonIdx))
                {
                    ConsoleSystem.Log("Invalid device or button index", ConsoleChannel.Error);
                    return false;
                }

                var pressed = args[2].ToLowerInvariant() == "press";
                var buttonIndex = (JoyButton)buttonIdx;

                var inputEvent = new InputEventJoypadButton
                {
                    Device = device,
                    ButtonIndex = buttonIndex,
                    Pressed = pressed
                };

                Input.ParseInputEvent(inputEvent);
                ConsoleSystem.Log($"[Input] Joypad button {buttonIndex} {(pressed ? "pressed" : "released")} on device {device}", ConsoleChannel.Input);
                return true;
            }
        ));

        console.RegisterCommand(new ConsoleCommand(
            "input_joy_axis",
            "Simulate controller axis movement",
            "input_joy_axis <device> <axis_index> <value>",
            async args =>
            {
                if (args.Length < 3)
                {
                    ConsoleSystem.Log("Usage: input_joy_axis <device> <axis_index> <value>", ConsoleChannel.Error);
                    ConsoleSystem.Log("Axes: 0=LeftX, 1=LeftY, 2=RightX, 3=RightY, 4=TriggerLeft, 5=TriggerRight", ConsoleChannel.Info);
                    ConsoleSystem.Log("Value range: -1.0 to 1.0", ConsoleChannel.Info);
                    return false;
                }

                if (!int.TryParse(args[0], out var device) ||
                    !int.TryParse(args[1], out var axisIdx) ||
                    !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    ConsoleSystem.Log("Invalid device, axis, or value", ConsoleChannel.Error);
                    return false;
                }

                var axis = (JoyAxis)axisIdx;
                value = Mathf.Clamp(value, -1.0f, 1.0f);

                var inputEvent = new InputEventJoypadMotion
                {
                    Device = device,
                    Axis = axis,
                    AxisValue = value
                };

                Input.ParseInputEvent(inputEvent);
                ConsoleSystem.Log($"[Input] Joypad axis {axis} = {value:F2} on device {device}", ConsoleChannel.Input);
                return true;
            }
        ));
    }

    private static void RegisterActionCommands(ConsoleSystem console)
    {
        console.RegisterCommand(new ConsoleCommand(
            "input_action_press",
            "Simulate pressing an input action",
            "input_action_press <action_name> [strength:1.0]",
            async args =>
            {
                if (args.Length < 1)
                {
                    ConsoleSystem.Log("Usage: input_action_press <action_name> [strength:1.0]", ConsoleChannel.Error);
                    return false;
                }

                var actionName = args[0];
                var strength = 1.0f;

                if (args.Length > 1 && args[1].ToLowerInvariant().StartsWith("strength:"))
                {
                    var val = args[1].Split(':')[1];
                    if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
                        strength = Mathf.Clamp(s, 0.0f, 1.0f);
                }

                if (!InputMap.HasAction(actionName))
                {
                    ConsoleSystem.Log($"Unknown action: {actionName}", ConsoleChannel.Error);
                    return false;
                }

                var inputEvent = new InputEventAction
                {
                    Action = actionName,
                    Pressed = true,
                    Strength = strength
                };

                Input.ParseInputEvent(inputEvent);
                ConsoleSystem.Log($"[Input] Action pressed: {actionName} (strength: {strength:F2})", ConsoleChannel.Input);
                return true;
            }
        ));

        console.RegisterCommand(new ConsoleCommand(
            "input_action_release",
            "Simulate releasing an input action",
            "input_action_release <action_name>",
            async args =>
            {
                if (args.Length < 1)
                {
                    ConsoleSystem.Log("Usage: input_action_release <action_name>", ConsoleChannel.Error);
                    return false;
                }

                var actionName = args[0];

                if (!InputMap.HasAction(actionName))
                {
                    ConsoleSystem.Log($"Unknown action: {actionName}", ConsoleChannel.Error);
                    return false;
                }

                var inputEvent = new InputEventAction
                {
                    Action = actionName,
                    Pressed = false,
                    Strength = 0.0f
                };

                Input.ParseInputEvent(inputEvent);
                ConsoleSystem.Log($"[Input] Action released: {actionName}", ConsoleChannel.Input);
                return true;
            }
        ));

        console.RegisterCommand(new ConsoleCommand(
            "input_list_actions",
            "List all available input actions",
            "input_list_actions",
            async _ =>
            {
                var actions = InputMap.GetActions();
                ConsoleSystem.Log($"Available input actions ({actions.Count}):", ConsoleChannel.Info);
                foreach (var action in actions)
                {
                    var actionStr = action.ToString();
                    // Skip internal UI actions
                    if (actionStr.StartsWith("ui_")) continue;
                    ConsoleSystem.Log($"  - {actionStr}", ConsoleChannel.Info);
                }
                return true;
            }
        ));
    }
}

