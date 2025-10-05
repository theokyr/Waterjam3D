using System;

namespace Waterjam.Core.Systems.Console;

public class ConsoleMessage
{
    public string Text { get; }
    public ConsoleChannel Channel { get; }
    public DateTime Timestamp { get; }

    public ConsoleMessage(string text, ConsoleChannel channel, DateTime timestamp)
    {
        Text = text;
        Channel = channel;
        Timestamp = timestamp;
    }
}