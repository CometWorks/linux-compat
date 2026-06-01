using System;
using Sandbox;
using Sandbox.Engine.Utils;

namespace ClientPlugin.Compatibility;

// Read/write custom keys in the game's SpaceEngineers.cfg. MyConfig's parameter
// accessors are protected; after publicizing VRage.Game we reach them directly
// so our windowed-size and windowed-position values are serialized in the same
// XML file and saved by the game's normal Save() path (on settings apply and
// on game exit).
internal static class PluginWindowConfig
{
    // KEY_WINDOWED_WIDTH and KEY_WINDOWED_HEIGHT are the same keys used by SE to store
    // game render resolution (they are equal in windowed mode).
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

    // MySandboxGame.OnExit does NOT call Config.Save(), so in-memory config
    // changes (including our SetWindowed*) only reach disk on specific flows
    // like video-settings-apply. To guarantee window geometry survives a
    // normal close, the plugin must trigger Save() itself.
    public static void Save()
    {
        try
        {
            MySandboxGame.Config?.Save();
        }
        catch (Exception)
        {
        }
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
            if (int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int value))
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
        catch (Exception)
        {
        }
    }
}
