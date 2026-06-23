using System;
using System.Collections.Generic;
using Sharp.Shared.Definition;

namespace EspPlayers.Utils;

/// <summary>
/// Color-code helpers for chat output. Locale strings ship {{double-braced}} tokens so they
/// survive <c>string.Format</c> inside <c>ILocale.Text</c>; the {single-braced} literal that
/// remains is converted to engine chat escape codes here.
/// </summary>
internal static class Format
{
    private static readonly Dictionary<string, string> ColorCache = new(StringComparer.OrdinalIgnoreCase)
    {
        { "{white}",      ChatColor.White },
        { "{default}",    ChatColor.White },
        { "{darkred}",    ChatColor.DarkRed },
        { "{pink}",       ChatColor.Pink },
        { "{green}",      ChatColor.Green },
        { "{lightgreen}", ChatColor.LightGreen },
        { "{lime}",       ChatColor.Lime },
        { "{red}",        ChatColor.Red },
        { "{grey}",       ChatColor.Grey },
        { "{gray}",       ChatColor.Grey },
        { "{yellow}",     ChatColor.Yellow },
        { "{gold}",       ChatColor.Gold },
        { "{silver}",     ChatColor.Silver },
        { "{blue}",       ChatColor.Blue },
        { "{lightblue}",  ChatColor.Blue },
        { "{darkblue}",   ChatColor.DarkBlue },
        { "{purple}",     ChatColor.Purple },
        { "{lightred}",   ChatColor.LightRed },
        { "{muted}",      ChatColor.Muted },
        { "{head}",       ChatColor.Head },
    };

    /// <summary>
    /// Replace color placeholders like {red}, {blue}, etc. with actual ChatColor codes.
    /// </summary>
    public static string ProcessColorCodes(string message)
    {
        if (string.IsNullOrEmpty(message))
            return message;

        if (!message.Contains('{'))
            return message;

        var result = message;
        foreach (var kvp in ColorCache)
        {
            if (result.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                result = result.Replace(kvp.Key, kvp.Value, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }
}
