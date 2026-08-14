using System;

namespace ClientPlugin.Compatibility;

// Rendering configuration passed in-process by the Remote plugin through an
// environment variable (not this plugin's own command line).
internal static class RenderingConfig
{
    // The Remote plugin's --no-render option sets PULSAR_NO_RENDER=true in the
    // process environment (native and managed): its preloader hook
    // (HeadlessEnvironment.cs) parses the game's command line in its Initialize
    // pre-hook, before this plugin's Finish hook reads it. When it is set,
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
