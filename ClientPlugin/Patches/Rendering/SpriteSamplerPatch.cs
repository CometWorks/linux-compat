using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using ClientPlugin.Tools;
using HarmonyLib;
using SharpDX.Direct3D11;
using VRage.Render11.Common;
using VRage.Render11.RenderContext;
using VRage.Render11.Resources;
using VRage.Render11.Sprites;

namespace ClientPlugin.Patches.Rendering;

// Sharpen UI sprites by rebinding Sprites.hlsl slot 2 after StandardSamplers.
[HarmonyPatch(typeof(MySpritesManager), nameof(MySpritesManager.OnDeviceInit))]
[HarmonyPatchCategory("Finish")]
static class SpriteSamplerCreatePatch
{
    static void Prefix()
    {
        var description = new SamplerStateDescription
        {
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            Filter = Filter.MinMagMipLinear,
            MipLodBias = -2f,
            MinimumLod = 0f,
            MaximumLod = float.MaxValue,
        };

        SpriteSamplerRenderPatch.Sampler = MyManagers.SamplerStates.CreateResource(
            "LinuxSprite",
            ref description
        );
    }
}

[HarmonyPatch(typeof(MySpritesManager), nameof(MySpritesManager.Render))]
[HarmonyPatchCategory("Finish")]
static class SpriteSamplerRenderPatch
{
    internal static ISamplerState Sampler;

    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase patchedMethod
    )
    {
        var il = instructions.ToList();
        il.RecordOriginalCode(patchedMethod);

        var setSamplers = AccessTools.Method(
            typeof(MyCommonStage),
            nameof(MyCommonStage.SetSamplers)
        );
        var indexes = il.FindAllIndex(instruction => instruction.Calls(setSamplers));
        if (indexes.Count != 1)
            throw new CodeInstructionNotFound(
                $"Expected one standard sampler bind in {patchedMethod.Name}, found {indexes.Count}"
            );

        il.InsertRange(
            indexes[0] + 1,
            [
                new CodeInstruction(OpCodes.Ldarg_3),
                new CodeInstruction(
                    OpCodes.Call,
                    AccessTools.Method(typeof(SpriteSamplerRenderPatch), nameof(BindSampler))
                ),
            ]
        );

        il.RecordPatchedCode(patchedMethod);
        return il;
    }

    static void BindSampler(MyRenderContext renderContext)
    {
        renderContext.PixelShader.SetSampler(2, Sampler);
    }
}
