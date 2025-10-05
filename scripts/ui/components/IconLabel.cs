using System.Collections.Generic;
using Godot;

namespace Waterjam.UI.Components;

/// <summary>
/// IconLabel is a lightweight label that displays an icon by name.
/// - In the future, this will render a glyph from a font-based icon set.
/// - Today, it uses a small built-in map and falls back to a plaintext "X" if the icon is unknown.
/// </summary>
public partial class IconLabel : Label
{
    [Export]
    public string IconName { get; set; } = string.Empty;

    [Export]
    public string FallbackText { get; set; } = "X";

    [Export]
    public bool UseCaptionStyle { get; set; } = true;

    private static readonly Dictionary<string, string> BuiltInIconMap = new()
    {
        // Safe ASCII-first hints; real project can replace with font glyphs later.
        { "play", ">" },
        { "pause", "||" },
        { "stop", "[]" },
        { "settings", "*" },
        { "gear", "*" },
        { "load", ">>" },
        { "save", "::" },
        { "back", "<" },
        { "quit", "x" },
        { "inventory", "[]" },
        { "journal", "J" },
        { "map", "M" },
        { "health", "+" },
        { "speed", "~" },
        { "fps", "#" },
        { "copy", "C" },
        { "bullet", "•" },
        { "check", "✓" },
        { "quest", "◆" }
    };

    public override void _Ready()
    {
        if (UseCaptionStyle)
        {
            ThemeTypeVariation = "CaptionLabel";
        }

        UpdateIcon();
    }

    public void SetIcon(string iconName)
    {
        IconName = iconName ?? string.Empty;
        UpdateIcon();
    }

    private void UpdateIcon()
    {
        var text = ResolveIcon(IconName);
        Text = text;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        // Slight letter spacing can help readability for ascii pseudo-icons
        AddThemeConstantOverride("outline_size", 0);
    }

    private string ResolveIcon(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return FallbackText;

        if (BuiltInIconMap.TryGetValue(name.Trim().ToLowerInvariant(), out var glyph))
            return string.IsNullOrEmpty(glyph) ? FallbackText : glyph;

        return FallbackText; // Fallback when missing
    }
}