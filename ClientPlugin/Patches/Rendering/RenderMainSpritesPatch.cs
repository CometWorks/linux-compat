using System;
using HarmonyLib;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Patches.Rendering;

// Render Linux UI at backbuffer resolution instead of the scaled 3D target,
// while preserving SpriteMainViewportScale centering.
[HarmonyPatch(typeof(MyRender11), "RenderMainSprites", new Type[0])]
[HarmonyPatchCategory("Finish")]
static class RenderMainSpritesPatch
{
    static bool Prefix()
    {
        var res = MyRender11.ResolutionI;
        var viewport = new MyViewport(res.X, res.Y);
        var viewportBound = viewport;

        var sceneResolution = MyRender11.ViewportResolution;
        if (sceneResolution.X > 0 && sceneResolution.Y > 0)
        {
            var scaledViewport = MyRender11.ScaleMainViewport(
                new MyViewport(sceneResolution.X, sceneResolution.Y)
            );
            var scaleX = res.X / (float)sceneResolution.X;
            var scaleY = res.Y / (float)sceneResolution.Y;
            viewportBound = new MyViewport(
                scaledViewport.OffsetX * scaleX,
                scaledViewport.OffsetY * scaleY,
                scaledViewport.Width * scaleX,
                scaledViewport.Height * scaleY
            );
        }

        var size = new Vector2(res.X, res.Y);
        MyRender11.RenderMainSprites(MyRender11.Backbuffer, viewportBound, viewport, size, null);
        return false;
    }
}
