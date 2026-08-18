using System.Reflection;
using ClientPlugin.Patches.PlatformGuards;
using HarmonyLib;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace ClientPlugin.Patches.WindowManagement;

// Publishes the cursor texture from the update thread to the render thread.
// Reference writes are atomic; value equality handles copied or interned strings.
internal static class CursorRenderRateState
{
    internal static volatile string LastCursorTextureName;
}

// Move the software cursor from its 60 Hz game-thread position to the latest
// SDL position when the render thread processes its sprite. Preserve size for HiDPI.
[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
static class CursorRenderRatePatch
{
    static MethodBase TargetMethod() =>
        AccessTools.Method("VRage.Render11.Sprites.MySpritesRenderer:ProcessDrawMessage");

    static bool Prepare() => TargetMethod() != null;

    static void Prefix(MyRenderMessageBase drawMessage)
    {
        if (drawMessage == null || drawMessage.MessageType != MyRenderMessageEnum.DrawSprite)
            return;

        // Match only the texture published by the cursor enqueuer.
        var cursorTexture = CursorRenderRateState.LastCursorTextureName;
        if (cursorTexture == null)
            return;

        var sprite = (MyRenderMessageDrawSprite)drawMessage;
        var spriteTexture = sprite.Texture;
        if (spriteTexture == null)
            return;
        if (!ReferenceEquals(spriteTexture, cursorTexture) && spriteTexture != cursorTexture)
            return;

        // Keep the queued position when SDL has no valid in-window snapshot.
        var sdlWindow = SdlInput2Provider.Instance;
        if (sdlWindow == null)
            return;

        if (!sdlWindow.TryGetFreshInWindowMousePosition(out Vector2 fresh))
            return;

        // Translate only; size retains the game thread's HiDPI scaling.
        RectangleF rect = sprite.DestinationRectangle;
        rect.X = fresh.X - rect.Width * 0.5f;
        rect.Y = fresh.Y - rect.Height * 0.5f;
        sprite.DestinationRectangle = rect;
    }
}
