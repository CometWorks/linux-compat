using System;
using Sandbox;

namespace ClientPlugin.Compatibility;

// Stores Linux window geometry in SpaceEngineers.cfg through protected MyConfig accessors.
internal static class PluginWindowConfig
{
    // Windowed size shares the game's render-resolution keys.
    private const string KEY_WINDOWED_WIDTH = "ScreenWidth";
    private const string KEY_WINDOWED_HEIGHT = "ScreenHeight";
    private const string KEY_WINDOWED_X = "LinuxCompat_WindowedX";
    private const string KEY_WINDOWED_Y = "LinuxCompat_WindowedY";

    public static bool TryGetWindowedSize(out int width, out int height)
    {
        width = 0;
        height = 0;
        int? w = GetInt(KEY_WINDOWED_WIDTH);
        int? h = GetInt(KEY_WINDOWED_HEIGHT);
        if (!w.HasValue || !h.HasValue || w.Value <= 0 || h.Value <= 0)
            return false;
        width = w.Value;
        height = h.Value;
        return true;
    }

    public static bool TryGetWindowedPosition(out int x, out int y)
    {
        x = 0;
        y = 0;
        int? px = GetInt(KEY_WINDOWED_X);
        int? py = GetInt(KEY_WINDOWED_Y);
        if (!px.HasValue || !py.HasValue)
            return false;
        x = px.Value;
        y = py.Value;
        return true;
    }

    public static void SetWindowedSize(int width, int height)
    {
        SetInt(KEY_WINDOWED_WIDTH, width);
        SetInt(KEY_WINDOWED_HEIGHT, height);
    }

    public static void SetWindowedPosition(int x, int y)
    {
        SetInt(KEY_WINDOWED_X, x);
        SetInt(KEY_WINDOWED_Y, y);
    }

    // MySandboxGame.OnExit does not save configuration.
    public static void Save()
    {
        try
        {
            MySandboxGame.Config?.Save();
        }
        catch (Exception) { }
    }

    private static int? GetInt(string key)
    {
        var config = MySandboxGame.Config;
        if (config == null)
            return null;
        try
        {
            var raw = config.GetParameterValue(key);
            if (string.IsNullOrEmpty(raw))
                return null;
            if (
                int.TryParse(
                    raw,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int value
                )
            )
                return value;
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void SetInt(string key, int value)
    {
        var config = MySandboxGame.Config;
        if (config == null)
            return;
        try
        {
            config.SetParameterValue(key, value);
        }
        catch (Exception) { }
    }
}
