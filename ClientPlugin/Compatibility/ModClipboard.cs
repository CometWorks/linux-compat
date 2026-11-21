using System;
using Sandbox.ModAPI;

namespace ClientPlugin.Compatibility;

/// <summary>
/// The system clipboard for mods (SE-1809). The mod API has no clipboard, so a mod sends a mod
/// message on <see cref="MessageId"/>: a string payload sets the clipboard, an
/// <c>Action&lt;string&gt;</c> payload is called back with its text on the game thread, a frame
/// or two later as SDL reads it on the render thread. Bugfixes answers the same messages on
/// Windows and leaves Linux to this class.
/// </summary>
internal static class ModClipboard
{
    // "CLIPBOAR" in ASCII
    public const long MessageId = 0x434C4950424F4152;

    public static void Register() =>
        MyAPIUtilities.Static.RegisterMessageHandler(MessageId, Handle);

    private static void Handle(object payload)
    {
        switch (payload)
        {
            case string text:
                SdlClipboard.SetText(text);
                break;
            case Action<string> reply:
                SdlClipboard.RequestText(text => reply(text ?? string.Empty));
                break;
        }
    }
}
