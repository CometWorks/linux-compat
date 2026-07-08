using System;

namespace ClientPlugin.Compatibility;

// Rendering configuration passed in-process by Pulsar through an environment
// variable (not this plugin's own command line).
internal static class RenderingConfig
{
    // Pulsar's --no-render sets PULSAR_NO_RENDER=true in the process
    // environment (see Pulsar's HeadlessMode.SetupEnvironment). When it is set,
    // the game runs without any 3D rendering: this plugin installs MyNullRender,
    // skips the DXVK/render initialization and teardown and the SDL render
    // thread, and suppresses the splash screen and cursor handling that assume a
    // real window. This is deliberately keyed on the environment variable rather
    // than the --headless argument so that plain --headless (offscreen
    // framebuffer rendering) keeps a working renderer.
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
