using System.Reflection;
using ClientPlugin.Patches.PlatformGuards;
using HarmonyLib;
using VRageMath;
using VRageRender;
using VRageRender.Messages;

namespace ClientPlugin.Patches.WindowManagement;

// Shared mutable state used to identify "the" cursor sprite among queued
// DrawSprite messages on the render thread. Set by the update thread inside
// MyDX9GuiDrawMouseCursorPatch right before MyGuiManager.DrawSpriteBatch
// enqueues the cursor sprite; read by the render-thread prefix in
// CursorRenderRatePatch.
//
// Reference assignments to a static field of reference type are atomic in
// .NET, so the cross-thread handoff needs no explicit synchronisation. The
// string instance written by the update thread is the same instance the
// render thread sees in MyRenderMessageDrawSprite.Texture (the game does
// not normalise or copy the path between MyRenderProxy.DrawSprite and the
// queued message), so reference equality is the cheap fast path; the
// fallback to value equality covers any future change that interns or
// rewrites the string in transit.
internal static class CursorRenderRateState
{
    internal static volatile string LastCursorTextureName;
}

// Make the software cursor track the real pointer at render-frame rate
// instead of being stuck at the 60 Hz update-tick rate.
//
// Background:
//   * Linux forces software cursor (MyVideoSettingsManager.IsHardwareCursorUsed
//     returns false). The visible pointer is a sprite drawn by the engine.
//   * MyDX9Gui.Draw enqueues the cursor sprite once per update tick on the
//     main thread, using mouse coordinates cached in
//     MyVRageInput.m_absoluteMousePosition (refreshed only inside
//     MyVRageInput.Update — i.e. once per 60 Hz tick).
//   * The plugin's SDL polling thread already updates SdlGameWindow's mouse
//     snapshot at ~1 kHz, so a far fresher coordinate is always available;
//     it just never reaches the rendered sprite.
//
// Fix: intercept MySpritesRenderer.ProcessDrawMessage on the render thread,
// detect the cursor's DrawSprite message by its texture name (recorded by
// MyDX9GuiDrawMouseCursorPatch right before enqueueing), and rewrite its
// destination rectangle's X/Y so the centre sits over the freshest in-window
// SDL coordinate. The rectangle's Width/Height — which encode HiDPI/scale
// baked at update time — are preserved.
//
// MySpritesRenderer is internal to VRage.Render11, so the target method is
// resolved via Harmony's TargetMethod() / AccessTools.Method by full name.
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

        // Match the message we're about to mutate against the texture the
        // update-thread cursor enqueuer recorded for the current tick.
        // Skipping when no cursor was enqueued (cursor hidden during
        // gameplay, mouse outside window, etc.) leaves unrelated sprites
        // alone.
        var cursorTexture = CursorRenderRateState.LastCursorTextureName;
        if (cursorTexture == null)
            return;

        var sprite = (MyRenderMessageDrawSprite)drawMessage;
        var spriteTexture = sprite.Texture;
        if (spriteTexture == null)
            return;
        if (!ReferenceEquals(spriteTexture, cursorTexture) && spriteTexture != cursorTexture)
            return;

        // Read the freshest SDL mouse position under the SDL buffer lock.
        // Bail out if the SDL game window has not been wired up yet (early
        // startup / splash) or the cursor is currently outside the window
        // — in either case we want to keep the original sprite position
        // baked by the update thread rather than teleport the cursor.
        var sdlWindow = SdlInput2Provider.Instance;
        if (sdlWindow == null)
            return;

        if (!sdlWindow.TryGetFreshInWindowMousePosition(out Vector2 fresh))
            return;

        // Only translate the rectangle; preserve the sized-at-update-tick
        // Width/Height so HiDPI/scale stay correct. The original draw used
        // HORISONTAL_CENTER_AND_VERTICAL_CENTER alignment so the rect is
        // centred on the cursor position — replicate that here.
        RectangleF rect = sprite.DestinationRectangle;
        rect.X = fresh.X - rect.Width * 0.5f;
        rect.Y = fresh.Y - rect.Height * 0.5f;
        sprite.DestinationRectangle = rect;
    }
}
