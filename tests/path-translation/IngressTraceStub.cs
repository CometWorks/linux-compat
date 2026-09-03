namespace ClientPlugin.Patches.PathHandling;

/// <summary>
/// Stands in for the real IngressTrace, which reaches into PathCache, PathHelpers and MyLog and
/// so cannot compile outside the game. PathTranslation only asks whether tracing is active.
/// </summary>
public static class IngressTrace
{
    public static bool Active => false;

    public static void Record(string input, string output) { }
}
