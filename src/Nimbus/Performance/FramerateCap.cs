using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Nimbus.Core;

namespace Nimbus.Performance;

/// <summary>
/// Roblox's own "Maximum Frame Rate" option, stored as &lt;int name="FramerateCap"&gt; in GlobalBasicSettings_13.xml.
/// Only that one value is changed; the rest of the file is left byte for byte as Roblox wrote it.
/// </summary>
internal static partial class FramerateCap
{
    private const string LogIdent = "FramerateCap";

    /// <summary>Common screen refresh rates. Roblox doesn't go above 240.</summary>
    public static readonly int[] Choices = [60, 120, 144, 165, 240];

    public static string DefaultFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "GlobalBasicSettings_13.xml");

    [GeneratedRegex("""<int name="FramerateCap">(-?\d+)</int>""")]
    private static partial Regex CapRegex();

    [GeneratedRegex("""(<Item class="UserGameSettings"[^>]*>\s*<Properties>)(\s*)""")]
    private static partial Regex PropertiesRegex();

    /// <summary>The value Roblox has saved right now, or null if the file or setting doesn't exist yet.</summary>
    public static int? Read(string file)
    {
        if (!File.Exists(file))
            return null;

        var match = CapRegex().Match(File.ReadAllText(file));
        return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Sets the cap. Returns false when there's nothing to change or Roblox has never created its settings file.</summary>
    public static bool Write(string file, int cap)
    {
        if (!File.Exists(file))
        {
            Logger.Info(LogIdent, "Roblox hasn't created its settings file yet; skipping");
            return false;
        }

        string text = File.ReadAllText(file);
        string value = cap.ToString(CultureInfo.InvariantCulture);
        string updated;

        var match = CapRegex().Match(text);
        if (match.Success)
        {
            if (match.Groups[1].Value == value)
                return false;

            updated = text[..match.Groups[1].Index] + value + text[(match.Groups[1].Index + match.Groups[1].Length)..];
        }
        else
        {
            // Not saved yet: add it as the first property, matching the file's own indentation.
            var properties = PropertiesRegex().Match(text);
            if (!properties.Success)
            {
                Logger.Warn(LogIdent, "Couldn't find UserGameSettings in Roblox's settings file; skipping");
                return false;
            }

            string indent = properties.Groups[2].Value;
            updated = text.Insert(properties.Index + properties.Groups[1].Length, $"{indent}<int name=\"FramerateCap\">{value}</int>");
        }

        File.WriteAllText(file, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Logger.Info(LogIdent, $"Roblox's Maximum Frame Rate set to {value}");
        return true;
    }
}
