using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ClientPlugin.Compatibility;
using HarmonyLib;
using Sandbox;
using Sandbox.Engine.Utils;
using SpaceEngineers.Game;
using VRage;
using VRage.UserInterface;
using VRageMath;
using VRageRender;

namespace ClientPlugin.Patches.Rendering;

[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
static class DxvkNativeResolverInitializePatch
{
    static bool Prepare()
    {
        return AccessTools.Method("SpaceEngineers.PlatformInitialization.MyDxvkNativeResolver:Initialize") != null;
    }

    static MethodBase TargetMethod()
    {
        return AccessTools.Method("SpaceEngineers.PlatformInitialization.MyDxvkNativeResolver:Initialize");
    }

    static bool Prefix()
    {
        if (RenderingConfig.AllowRendering)
            return true;

        Console.WriteLine("[LinuxCompat] rendering disabled (PULSAR_NO_RENDER); skipping DXVK native resolver initialization");
        return false;
    }
}

[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
static class MyProgramInitializeRenderPatch
{
    private const int HeadlessWidth = 640;
    private const int HeadlessHeight = 480;
    internal static readonly IVRageWindow Window = new HeadlessWindow(HeadlessWidth, HeadlessHeight);

    static bool Prepare()
    {
        return TargetMethod() != null;
    }

    static MethodBase TargetMethod()
    {
        return GetSpaceEngineersProgramType()?.GetMethod(
            "InitializeRender",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
    }

    private static Type GetSpaceEngineersProgramType()
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(asm => string.Equals(asm.GetName().Name, "SpaceEngineers", StringComparison.OrdinalIgnoreCase));

        if (assembly == null)
        {
            try { assembly = Assembly.Load("SpaceEngineers"); }
            catch { return null; }
        }

        return assembly.GetType("SpaceEngineers.MyProgram");
    }

    static bool Prefix()
    {
        if (RenderingConfig.AllowRendering)
            return true;

        Console.WriteLine("[LinuxCompat] rendering disabled (PULSAR_NO_RENDER); using MyNullRender");
        MyFakes.USE_NULL_AUDIO_DRIVER = true;
        MyFakes.USE_NULL_INPUT_DRIVER = true;
        InstallHeadlessWindow(null);
        _ = new MyEngine();
        MyRenderProxy.Initialize(new MyNullRender());
        MySandboxGame.UpdateScreenSize(HeadlessWidth, HeadlessHeight, new MyViewport(0, 0, HeadlessWidth, HeadlessHeight));
        return false;
    }

    internal static void InstallHeadlessWindow(MySandboxGame game)
    {
        var windows = MyVRage.Platform.Windows;
        var windowsType = windows.GetType();

        AccessTools.PropertySetter(windowsType, "Window")
            ?.Invoke(windows, [Window]);

        AccessTools.PropertySetter(windowsType, "WindowHandle")
            ?.Invoke(windows, [IntPtr.Zero]);

        if (game != null)
        {
            AccessTools.Field(typeof(MySandboxGame), "form")
                ?.SetValue(game, Window);
        }
    }
}

[HarmonyPatch(typeof(SpaceEngineersGame), "InitializeRender")]
[HarmonyPatchCategory("Finish")]
static class SpaceEngineersGameInitializeRenderPatch
{
    static bool Prefix(SpaceEngineersGame __instance)
    {
        if (RenderingConfig.AllowRendering)
            return true;

        Console.WriteLine("[LinuxCompat] rendering disabled (PULSAR_NO_RENDER); skipping game render component initialization");
        MyProgramInitializeRenderPatch.InstallHeadlessWindow(__instance);
        return false;
    }
}

[HarmonyPatch]
[HarmonyPatchCategory("Finish")]
static class HeadlessGravityIndicatorDrawPatch
{
    static bool Prepare()
    {
        return TargetMethod() != null;
    }

    static MethodBase TargetMethod()
    {
        return AccessTools.Method("Sandbox.Game.Screens.Helpers.MyHudControlGravityIndicator:Draw");
    }

    static bool Prefix()
    {
        return RenderingConfig.AllowRendering;
    }
}

sealed class HeadlessWindow : IVRageWindow
{
    private readonly Vector2I _size;

    public HeadlessWindow(int width, int height)
    {
        _size = new Vector2I(width, height);
    }

    public bool DrawEnabled => false;
    public bool IsActive => true;
    public Vector2I ClientSize => _size;
    public Vector2I ClientSizePixels => _size;

    public event Action OnExit { add { } remove { } }
    public event Action OnManualWindowCloseRequest { add { } remove { } }

    public void CloseManually() { }
    public void DoEvents() { }
    public void Exit() { }
    public bool UpdateRenderThread() => false;
    public void UpdateMainThread() { }
    public void SetCursor(Stream stream) { }
    public void AddMessageHandler(uint wm, ActionRef<MyMessage> action) { }
    public void RemoveMessageHandler(uint wm, ActionRef<MyMessage> action) { }
    public void SetClientSize(int width, int height) { }
    public void ShowAndFocus() { }
    public void Hide() { }
}
