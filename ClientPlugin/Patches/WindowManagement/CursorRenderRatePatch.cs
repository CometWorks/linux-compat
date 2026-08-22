using ClientPlugin.Patches.PlatformGuards;
using HarmonyLib;
using VRage.Render11.Sprites;
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
// SDL position when the render thread processes its sprite.
[HarmonyPatch(typeof(MySpritesRenderer), nameof(MySpritesRenderer.ProcessDrawMessage))]
[HarmonyPatchCategory("Finish")]
static class CursorRenderRatePatch
{
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

        Vector2I windowSize = sdlWindow.ClientSize;
        Vector2I renderSize = MyRender11.ResolutionI;
        if (windowSize.X <= 0 || windowSize.Y <= 0 || renderSize.X <= 0 || renderSize.Y <= 0)
            return;
        fresh.X *= renderSize.X / (float)windowSize.X;
        fresh.Y *= renderSize.Y / (float)windowSize.Y;

        // SDL input is in logical coordinates; the sprite uses the current render size.
        RectangleF rect = sprite.DestinationRectangle;
        rect.X = fresh.X - rect.Width * 0.5f;
        rect.Y = fresh.Y - rect.Height * 0.5f;
        sprite.DestinationRectangle = rect;
    }
}
