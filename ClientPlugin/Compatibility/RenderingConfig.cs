using System;

namespace ClientPlugin.Compatibility;

// Rendering configuration passed in-process by Pulsar through an environment
// variable (not this plugin's own command line).
internal static class RenderingConfig
{
    // PULSAR_NO_RENDER disables DXVK and SDL window paths. Plain --headless
    // retains offscreen rendering and does not set this variable.
    internal static bool AllowRendering { get; } =
        !IsTruthy(Environment.GetEnvironmentVariable("PULSAR_NO_RENDER"));

    private static bool IsTruthy(string value) =>
        value != null
        && value.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            _ => false,
        };
}
