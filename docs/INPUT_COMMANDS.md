# Console Input Commands

Console commands for injecting input events into the game. These commands allow AI agents to operate the game autonomously via the MCP inbox system.

## Overview

All input commands can be sent via:
1. **In-game console** (press `` ` `` or `~`)
2. **MCP inbox** - Write command to `%APPDATA%\Godot\app_userdata\Waterjam3D\mcp\inbox\command.txt`
3. **Command line** - Use `+command_name args` when launching the game

## Keyboard Input

### input_key_press
Simulate a key press event.

```
input_key_press <keycode> [physical:true|false]
```

**Examples:**
```
input_key_press W physical:true
input_key_press Space
input_key_press Escape physical:false
```

**Common keycodes:** W, A, S, D, Space, Enter, Escape, Shift, Ctrl, Alt, Tab, etc.

### input_key_release
Simulate a key release event.

```
input_key_release <keycode> [physical:true|false]
```

**Examples:**
```
input_key_release W physical:true
input_key_release Space
```

### input_key_tap
Simulate a quick key press and release (hold for specified duration).

```
input_key_tap <keycode> [physical:true|false] [hold_ms:100]
```

**Examples:**
```
input_key_tap Space
input_key_tap W physical:true hold_ms:50
input_key_tap Enter hold_ms:200
```

## Mouse Input

### input_mouse_move
Simulate mouse movement to screen coordinates.

```
input_mouse_move <x> <y> [relative:true|false]
```

**Examples:**
```
input_mouse_move 640 360
input_mouse_move 10 20 relative:true
input_mouse_move 1920 1080
```

### input_mouse_button
Simulate mouse button press or release.

```
input_mouse_button <button_index> <press|release> [x] [y]
```

**Button indices:**
- 1 = Left button
- 2 = Right button
- 3 = Middle button
- 4 = Wheel up
- 5 = Wheel down

**Examples:**
```
input_mouse_button 1 press
input_mouse_button 1 release
input_mouse_button 2 press 640 360
input_mouse_button 3 press
```

### input_mouse_click
Simulate a complete mouse click (press + release).

```
input_mouse_click <button_index> [x] [y] [hold_ms:100]
```

**Examples:**
```
input_mouse_click 1
input_mouse_click 1 640 360
input_mouse_click 2 hold_ms:200
input_mouse_click 1 100 100 hold_ms:50
```

### input_mouse_wheel
Simulate mouse wheel scrolling.

```
input_mouse_wheel <delta> [x] [y]
```

Positive delta scrolls up, negative scrolls down.

**Examples:**
```
input_mouse_wheel 1.0
input_mouse_wheel -1.0
input_mouse_wheel 2.0 640 360
```

## Controller Input

### input_joy_button
Simulate controller button press or release.

```
input_joy_button <device> <button_index> <press|release>
```

**Common button indices:**
- 0 = A/Cross
- 1 = B/Circle
- 2 = X/Square
- 3 = Y/Triangle
- 4 = L1/LB
- 5 = R1/RB
- 6 = L2/LT
- 7 = R2/RT
- 8 = Select/Back
- 9 = Start
- 10 = L3 (Left stick button)
- 11 = R3 (Right stick button)

**Examples:**
```
input_joy_button 0 0 press
input_joy_button 0 0 release
input_joy_button 0 3 press
```

### input_joy_axis
Simulate controller axis movement.

```
input_joy_axis <device> <axis_index> <value>
```

**Axis indices:**
- 0 = Left stick X
- 1 = Left stick Y
- 2 = Right stick X
- 3 = Right stick Y
- 4 = Left trigger
- 5 = Right trigger

Value range: -1.0 to 1.0

**Examples:**
```
input_joy_axis 0 0 1.0
input_joy_axis 0 1 -0.5
input_joy_axis 0 4 0.8
```

## Input Actions

### input_action_press
Simulate pressing an input action.

```
input_action_press <action_name> [strength:1.0]
```

**Examples:**
```
input_action_press move_forward
input_action_press jump
input_action_press sprint strength:0.5
```

### input_action_release
Simulate releasing an input action.

```
input_action_release <action_name>
```

**Examples:**
```
input_action_release move_forward
input_action_release jump
```

### input_list_actions
List all available input actions (excludes UI actions).

```
input_list_actions
```

## Using Commands via MCP

### Method 1: MCP Inbox (Preferred for Agents)

AI agents can write commands to the MCP inbox directory:

```
%APPDATA%\Godot\app_userdata\Waterjam3D\mcp\inbox\
```

The game polls this directory every 0.5 seconds and executes any commands found.

**Example (PowerShell):**
```powershell
echo "input_key_tap Space" > "$env:APPDATA\Godot\app_userdata\Waterjam3D\mcp\inbox\cmd1.txt"
```

**Example (Python):**
```python
import os
inbox = os.path.join(os.environ['APPDATA'], 'Godot', 'app_userdata', 'Waterjam3D', 'mcp', 'inbox')
with open(os.path.join(inbox, 'command.txt'), 'w') as f:
    f.write('input_key_tap Space')
```

### Method 2: MCP Godot Tools

Using the MCP Godot tools:

```python
mcp_godot_send_console_command(
    projectPath="C:\\Projects\\Godot\\Waterjam3D-game",
    command="input_key_tap W physical:true",
    fallbackRun=True
)
```

### Method 3: Command Line

Start the game with commands:

```
Godot.exe +input_key_tap Space +map res://scenes/dev/dev.tscn
```

## Agent Usage Examples

### Walking Forward
```
input_key_press W physical:true
# Wait 1 second
input_key_release W physical:true
```

### Opening Menu
```
input_key_tap Escape
```

### Clicking a Button at Position
```
input_mouse_move 640 360
input_mouse_click 1 hold_ms:100
```

### Moving with Controller
```
input_joy_axis 0 0 1.0    # Right on left stick
input_joy_axis 0 1 -0.5   # Up on left stick (half)
# Wait 1 second
input_joy_axis 0 0 0.0    # Center left stick
input_joy_axis 0 1 0.0
```

### Jumping
```
input_action_press jump
# Wait 100ms
input_action_release jump
```

## Notes

- All commands are executed on the main thread
- Commands with delays (like `input_key_tap`) use `Task.Delay` internally
- Mouse coordinates are in screen space (0,0 = top-left)
- Physical keycodes use hardware scancodes (keyboard layout independent)
- Input actions must be defined in the project's Input Map
- The MCP inbox is checked every 0.5 seconds
- Commands are logged to the console with `ConsoleChannel.Input`

