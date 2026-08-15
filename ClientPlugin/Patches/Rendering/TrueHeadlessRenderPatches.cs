using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ClientPlugin.Compatibility;
using ClientPlugin.Patches.PlatformGuards;
using HarmonyLib;
using Sandbox;
using Sandbox.Engine.Utils;
using SpaceEngineers.Game;
using VRage;
using VRage.Input;
using VRage.Platform.Windows;
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
    internal static readonly HeadlessWindow Window = new HeadlessWindow(HeadlessWidth, HeadlessHeight);

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
        InstallHeadlessWindow(null);
        InstallHeadlessInput();
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

    // Install the headless window as the platform input device, the way
    // CreateWindowPatch installs the SDL window when rendering. Without a
    // platform input MySandboxGame.InitInput would have to fall back to
    // MyNullInput (USE_NULL_INPUT_DRIVER), and the Remote plugin injects by
    // patching MyVRageInput.Update, which MyNullInput never reaches — injected
    // keys would be accepted and silently dropped. MyVRageInput dereferences
    // both MyVRage.Platform.Input and .Input2 every frame, so both must be set
    // here: InitializeRenderThread, which normally does it, never runs without
    // rendering.
    internal static void InstallHeadlessInput()
    {
        var setter = AccessTools.PropertySetter(typeof(MyVRagePlatform), "Input");
        if (setter == null || MyVRage.Platform is not MyVRagePlatform platform)
        {
            // Should not happen, but a null platform input would be an NRE per
            // frame in MyVRageInput.Update. Fall back to the null input driver:
            // no injected input (as before this was fixed), but a running game.
            Console.WriteLine("[LinuxCompat] WARNING: cannot install the headless platform input; using MyNullInput, injected input will not work");
            MyFakes.USE_NULL_INPUT_DRIVER = true;
            return;
        }

        setter.Invoke(platform, [Window]);
        SdlInput2Provider.Input2 = Window;
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

// Stand-in for SdlGameWindow on the PULSAR_NO_RENDER path: a window that draws
// nothing and an input device that reads no hardware. It implements the same
// three interfaces as SdlGameWindow (IVRageWindow, IVRageInput, IVRageInput2)
// so the game builds a real MyVRageInput around it; every frame the harness
// cares about arrives through the Remote plugin's MyVRageInput.Update patch,
// so nothing here ever needs to report a real key, button or axis.
sealed class HeadlessWindow : IVRageWindow, IVRageInput, IVRageInput2
{
    private static readonly uint[] NoDeveloperKeys = new uint[4];

    private readonly Vector2I _size;

    // Buffered text input, swapped out by GetBufferedTextInput the same way
    // SdlGameWindow does it. Nothing calls AddChar without a real window, but
    // the swap must still clear the caller's buffer each frame — otherwise
    // injected text would be re-delivered on every subsequent frame.
    private readonly object _inputLock = new object();
    private List<char> _bufferedChars = new List<char>();

    public HeadlessWindow(int width, int height)
    {
        _size = new Vector2I(width, height);
        MousePosition = new Vector2(width * 0.5f, height * 0.5f);
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

    // IVRageInput. MyVRageInput reads MousePosition/MouseAreaSize every frame
    // and writes MousePosition back (SetMousePosition, joystick-as-mouse), so
    // the position is stored rather than discarded. It starts centered so the
    // GUI cursor coordinates are inside the client area from the first frame.
    public Vector2 MousePosition { get; set; }
    public Vector2 MouseAreaSize => new Vector2(_size.X, _size.Y);
    public bool MouseCapture { get; set; }
    public bool ShowCursor { get; set; } = true;
    public int KeyboardDelay => 0;
    public int KeyboardSpeed => 31;

    public void AddChar(char ch)
    {
        lock (_inputLock)
        {
            _bufferedChars.Add(ch);
        }
    }

    public void GetBufferedTextInput(ref List<char> currentTextInput)
    {
        currentTextInput.Clear();
        lock (_inputLock)
        {
            var temp = currentTextInput;
            currentTextInput = _bufferedChars;
            _bufferedChars = temp;
        }
    }

    // IVRageInput2. All no-ops reporting "nothing connected, nothing pressed".
    // DeveloperKeys must be a 4-element array (MyVRageInput.UpdateStates hashes
    // it every frame); zeros match what SdlGameWindow reports.
    uint[] IVRageInput2.DeveloperKeys => NoDeveloperKeys;
    bool IVRageInput2.IsCorrectlyInitialized => true;

    void IVRageInput2.GetMouseState(out MyMouseState state) => state = default;

    List<string> IVRageInput2.EnumerateJoystickNames() => new List<string>();
    string IVRageInput2.InitializeJoystickIfPossible(string joystickInstanceName) => null;
    bool IVRageInput2.IsJoystickAxisSupported(MyJoystickAxesEnum axis) => false;
    bool IVRageInput2.IsJoystickConnected() => false;
    void IVRageInput2.GetJoystickState(ref MyJoystickState state) { }
    void IVRageInput2.ShowVirtualKeyboardIfNeeded(Action<string> onSuccess, Action onCancel, string defaultText, string title, int maxLength) { }

    // MyGuiLocalizedKeyboardState.GetCurrentState calls this into a
    // MyKeyboardBuffer (32 bytes, fixed). Report no keys down; the injected
    // keyboard state is written straight into MyVRageInput by the Remote
    // plugin's postfix, which runs with OverrideUpdate set so nothing here
    // overwrites it.
    unsafe void IVRageInput2.GetAsyncKeyStates(byte* data)
    {
        for (int i = 0; i < 32; i++)
            data[i] = 0;
    }

    void IDisposable.Dispose() { }
}
