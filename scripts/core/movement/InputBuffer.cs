using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class InputBuffer : Node
{
    private Dictionary<string, BufferedInput> bufferedInputs = new();
    private const float DefaultBufferTime = 0.15f;

    private struct BufferedInput
    {
        public string ActionName;
        public double BufferEndTime;
        public bool WasPressed;
    }

    public override void _Process(double delta)
    {
        // Clean up expired buffers
        var expiredKeys = bufferedInputs
            .Where(kvp => Time.GetTicksMsec() / 1000.0 > kvp.Value.BufferEndTime)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            bufferedInputs.Remove(key);
        }
    }

    public void BufferInput(string action)
    {
        bufferedInputs[action] = new BufferedInput
        {
            ActionName = action,
            BufferEndTime = Time.GetTicksMsec() / 1000.0 + DefaultBufferTime,
            WasPressed = true
        };
    }

    public bool IsBuffered(string action)
    {
        if (bufferedInputs.TryGetValue(action, out var bufferedInput))
        {
            if (Time.GetTicksMsec() / 1000.0 < bufferedInput.BufferEndTime)
            {
                bufferedInputs.Remove(action);
                return true;
            }
            else
            {
                bufferedInputs.Remove(action);
            }
        }
        return false;
    }

    public bool ConsumeBufferedInput(string action)
    {
        if (IsBuffered(action))
        {
            return true;
        }
        return false;
    }

    public void ClearBuffer(string action)
    {
        bufferedInputs.Remove(action);
    }

    public void ClearAllBuffers()
    {
        bufferedInputs.Clear();
    }
}
