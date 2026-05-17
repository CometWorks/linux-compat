using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using VRage;
using VRage.Utils;
using VRageMath;
using VRageRender;
using Device1 = SharpDX.Direct3D11.Device1;

namespace ClientPlugin.Patches.Rendering;

[HarmonyPatch("VRage.Platform.Windows.Render.MyPlatformRender", "CreateAdaptersList")]
[HarmonyPatchCategory("Finish")]
static class CreateAdaptersListPatch
{
    static bool Prefix()
    {
        CreateLinuxAdaptersList();
        return false;
    }

    static Vector2I QueryLinuxDesktopResolution()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "xrandr",
                Arguments = "--current",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(startInfo);
            if (process == null) return Vector2I.Zero;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) return Vector2I.Zero;
            var match = Regex.Match(output, @"current\s+(\d+)\s*x\s*(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out int w) && int.TryParse(match.Groups[2].Value, out int h) && w > 0 && h > 0)
                return new Vector2I(w, h);
        }
        catch
        {
        }
        return Vector2I.Zero;
    }

    static List<string> QueryLinuxGPUs()
    {
        var gpus = new List<string>();
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "lspci",
                Arguments = "-d ::0300",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(startInfo);
            if (process == null) return gpus;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) return gpus;
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(": ", 2, StringSplitOptions.None);
                if (parts.Length == 2)
                {
                    var text = Regex.Replace(parts[1].Trim(), "\\s+\\(rev [^)]+\\)$", "");
                    var gpuName = Regex.Matches(text, "\\[([^\\]]+)\\]")
                        .Cast<Match>()
                        .Select(match => match.Groups[1].Value.Trim())
                        .MaxBy(bracketText => bracketText.Length);
                    gpus.Add(gpuName ?? text);
                }
            }
        }
        catch
        {
        }
        return gpus;
    }

    static readonly MyRefreshRatePriorityComparer m_refreshRatePriorityComparer = new MyRefreshRatePriorityComparer();

    static int VendorPriority(VendorIds vendorId)
    {
        switch (vendorId)
        {
            case VendorIds.Amd:
            case VendorIds.Nvidia:
                return 2;
            case VendorIds.Intel:
                return 1;
            default:
                return 0;
        }
    }

    static MyDisplayMode[] QueryAdapterDisplayModes(Factory factory, int adapterOrdinal)
    {
        try
        {
            if (factory == null || adapterOrdinal < 0 || adapterOrdinal >= factory.Adapters.Length)
                return null;

            var adapter = factory.Adapters[adapterOrdinal];
            var output = adapter.Outputs.FirstOrDefault();
            if (output == null)
                return null;

            var modeList = output.GetDisplayModeList(Format.R8G8B8A8_UNorm_SRgb, DisplayModeEnumerationFlags.Interlaced);
            if (modeList == null || modeList.Length == 0)
                return null;

            var modes = new MyDisplayMode[modeList.Length];
            for (int m = 0; m < modeList.Length; m++)
            {
                var md = modeList[m];
                modes[m] = new MyDisplayMode
                {
                    Height = md.Height,
                    Width = md.Width,
                    RefreshRate = md.RefreshRate.Numerator,
                    RefreshRateDenominator = md.RefreshRate.Denominator,
                };
            }
            Array.Sort(modes, m_refreshRatePriorityComparer);
            return modes;
        }
        catch
        {
            return null;
        }
    }

    static void FillFallbackDisplayModes(MyAdapterInfo[] adaptersList)
    {
        MyDisplayMode[] fallback = null;
        foreach (var info in adaptersList)
        {
            if (info.IsOutputAttached && info.SupportedDisplayModes != null)
            {
                fallback = info.SupportedDisplayModes;
                break;
            }
        }
        if (fallback == null)
        {
            fallback = new[]
            {
                new MyDisplayMode(640, 480, 60000, 1000),
                new MyDisplayMode(720, 576, 60000, 1000),
                new MyDisplayMode(800, 600, 60000, 1000),
                new MyDisplayMode(1024, 768, 60000, 1000),
                new MyDisplayMode(1152, 864, 60000, 1000),
                new MyDisplayMode(1280, 720, 60000, 1000),
                new MyDisplayMode(1280, 768, 60000, 1000),
                new MyDisplayMode(1280, 800, 60000, 1000),
                new MyDisplayMode(1280, 960, 60000, 1000),
                new MyDisplayMode(1280, 1024, 60000, 1000),
                new MyDisplayMode(1360, 768, 60000, 1000),
                new MyDisplayMode(1360, 1024, 60000, 1000),
                new MyDisplayMode(1440, 900, 60000, 1000),
                new MyDisplayMode(1600, 900, 60000, 1000),
                new MyDisplayMode(1600, 1024, 60000, 1000),
                new MyDisplayMode(1600, 1200, 60000, 1000),
                new MyDisplayMode(1680, 1200, 60000, 1000),
                new MyDisplayMode(1680, 1050, 60000, 1000),
                new MyDisplayMode(1920, 1080, 60000, 1000),
                new MyDisplayMode(1920, 1200, 60000, 1000),
            };
        }
        for (int i = 0; i < adaptersList.Length; i++)
        {
            if (adaptersList[i].SupportedDisplayModes == null)
            {
                var value = adaptersList[i];
                value.SupportedDisplayModes = fallback;
                adaptersList[i] = value;
            }
        }
    }

    static void CreateLinuxAdaptersList()
    {
        var desktopRes = QueryLinuxDesktopResolution();
        if (desktopRes.X <= 0 || desktopRes.Y <= 0)
            desktopRes = new Vector2I(1920, 1080);

        var rectangle = new Rectangle(0, 0, desktopRes.X, desktopRes.Y);
        var gpus = QueryLinuxGPUs();
        if (gpus.Count == 0)
            gpus.Add("DXVK Native Adapter");

        var platformRenderType = AccessTools.TypeByName("VRage.Platform.Windows.Render.MyPlatformRender");
        var adaptersList = new MyAdapterInfo[gpus.Count];
        var adapterModes = AccessTools.StaticFieldRefAccess<Dictionary<int, ModeDescription[]>>(platformRenderType, "m_adapterModes");
        adapterModes.Clear();

        var factoryField = AccessTools.Field(platformRenderType, "m_factory");
        var factory = (Factory)factoryField.GetValue(null) ?? new Factory1();
        factoryField.SetValue(null, factory);

        for (int j = 0; j < gpus.Count; j++)
        {
            var name = gpus[j];
            var vendorId = (VendorIds)0;
            if (name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0)
                vendorId = VendorIds.Nvidia;
            else if (name.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("ATI", StringComparison.OrdinalIgnoreCase) >= 0)
                vendorId = VendorIds.Amd;
            else if (name.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0)
                vendorId = VendorIds.Intel;

            // Query the adapter at the same ordinal in DXGI; if there are fewer DXGI adapters than
            // GPUs reported by lspci, fall back to adapter 0 (DXVK typically exposes a single adapter).
            int dxgiOrdinal = j < factory.Adapters.Length ? j : 0;
            var supportedDisplayModes = QueryAdapterDisplayModes(factory, dxgiOrdinal);

            adapterModes[j] = new ModeDescription[0];
            adaptersList[j] = new MyAdapterInfo
            {
                Name = name,
                DeviceName = name,
                OutputName = "SDL3",
                Description = "DXVK-backed D3D11 adapter on Linux",
                AdapterDeviceId = j,
                OutputId = 0,
                DesktopBounds = rectangle,
                DesktopResolution = new Vector2I(rectangle.Width, rectangle.Height),
                MaxTextureSize = 16384,
                Has512MBRam = true,
                IsDx11Supported = true,
                Priority = VendorPriority(vendorId),
                IsOutputAttached = true,
                VRAM = 4294967296uL,
                SVRAM = 4294967296uL,
                MultithreadedRenderingSupported = true,
                VendorId = vendorId,
                DeviceId = 0,
                DriverVersion = "DXVK",
                DriverDate = string.Empty,
                DriverUpdateNecessary = false,
                DriverUpdateLink = string.Empty,
                IsNvidiaNotebookGpu = false,
                AftermathSupported = false,
                Quality = MyRenderPresetEnum.HIGH,
                Mobile = false,
                ParallelVertexBufferMapping = true,
                DeferredTransferData = false,
                BatchedConstantBufferMapping = true,
                SupportedDisplayModes = supportedDisplayModes,
            };
        }

        FillFallbackDisplayModes(adaptersList);

        AccessTools.StaticFieldRefAccess<MyAdapterInfo[]>(platformRenderType, "m_adapterInfoList") = adaptersList;
    }
}


[HarmonyPatch("VRage.Platform.Windows.Render.MyPlatformRender", "GetAdapter")]
[HarmonyPatchCategory("Finish")]
static class GetAdapterPatch
{
    static readonly Type PlatformRenderType = AccessTools.TypeByName("VRage.Platform.Windows.Render.MyPlatformRender");

    static bool Prefix(int adapterOrdinal, ref Adapter adapter, ref MyAdapterInfo adapterInfo)
    {
        var factoryField = AccessTools.Field(PlatformRenderType, "m_factory");
        var factory = (Factory)factoryField.GetValue(null) ?? new Factory1();
        factoryField.SetValue(null, factory);
        var adaptersList = AccessTools.StaticFieldRefAccess<MyAdapterInfo[]>(PlatformRenderType, "m_adapterInfoList");
        int adapterDeviceId = adaptersList[adapterOrdinal].AdapterDeviceId;

        if (adapterDeviceId >= factory.Adapters.Length)
            adapterDeviceId = 0;

        if (factory.Adapters.Length == 0)
        {
            adapter = null;
        }
        else
        {
            adapter = factory.Adapters[adapterDeviceId];
        }
        adapterInfo = adaptersList[adapterOrdinal];
        return false;
    }
}

[HarmonyPatch("VRage.Platform.Windows.Render.MyPlatformRender", "FixSettings")]
[HarmonyPatchCategory("Finish")]
static class FixSettingsPatch
{
    static bool Prefix(ref MyRenderDeviceSettings settings)
    {
        var adaptersList = AccessTools.StaticFieldRefAccess<MyAdapterInfo[]>(
            AccessTools.TypeByName("VRage.Platform.Windows.Render.MyPlatformRender"), "m_adapterInfoList");

        var currentAdapter = adaptersList[settings.AdapterOrdinal];
        int refreshRate = settings.WindowMode != MyWindowModeEnum.FullscreenWindow ? settings.RefreshRate : 0;
        bool validRes = currentAdapter.GetDisplayMode(settings.BackBufferWidth, settings.BackBufferHeight, refreshRate).HasValue;

        if (settings.WindowMode != MyWindowModeEnum.Window && !validRes)
        {
            settings.BackBufferWidth = currentAdapter.DesktopResolution.X;
            settings.BackBufferHeight = currentAdapter.DesktopResolution.Y;
            settings.RefreshRate = 60000;
        }

        return false;
    }
}

[HarmonyPatch("VRage.Platform.Windows.Render.MyPlatformRender", "GetDefaultDeviceSettings")]
[HarmonyPatchCategory("Finish")]
static class GetDefaultDeviceSettingsPatch
{
    static bool Prefix(ref MyRenderDeviceSettings __result)
    {
        var adaptersList = AccessTools.StaticFieldRefAccess<MyAdapterInfo[]>(
            AccessTools.TypeByName("VRage.Platform.Windows.Render.MyPlatformRender"), "m_adapterInfoList");

        var displayMode = adaptersList[0].GetDisplayMode(
            adaptersList[0].DesktopResolution.X, adaptersList[0].DesktopResolution.Y, 0);

        __result = new MyRenderDeviceSettings
        {
            AdapterOrdinal = 0,
            BackBufferWidth = adaptersList[0].DesktopResolution.X,
            BackBufferHeight = adaptersList[0].DesktopResolution.Y,
            WindowMode = MyWindowModeEnum.Window,
            RefreshRate = (int)(displayMode.HasValue ? displayMode.Value.RefreshRateF * 1000f : 60000f),
            VSync = 0,
            DRSSettingsPresets = new MyDRSSettings[3]
            {
                new MyDRSSettings(),
                new MyDRSSettings(),
                new MyDRSSettings()
            }
        };
        return false;
    }
}

[HarmonyPatch("VRage.Platform.Windows.Render.MyPlatformRender", "CreateSwapChain")]
[HarmonyPatchCategory("Finish")]
static class CreateSwapChainPatch
{
    static readonly Type PlatformRenderType = AccessTools.TypeByName("VRage.Platform.Windows.Render.MyPlatformRender");

    static bool Prefix(IntPtr windowHandle)
    {
        AccessTools.Method(PlatformRenderType, "DisposeSwapChain")?.Invoke(null, null);

        var swapChainField = AccessTools.Field(PlatformRenderType, "m_swapchain");
        if (swapChainField.GetValue(null) != null)
            return false;

        var settings = (MyRenderDeviceSettings)AccessTools.Field(PlatformRenderType, "m_settings").GetValue(null);
        var deviceInstance = (SharpDX.Direct3D11.Device)AccessTools.Property(PlatformRenderType, "DeviceInstance").GetValue(null);

        var modeDesc = new ModeDescription
        {
            Format = Format.R8G8B8A8_UNorm_SRgb,
            Width = settings.BackBufferWidth,
            Height = settings.BackBufferHeight,
            RefreshRate = new Rational(0, 0),
            Scaling = DisplayModeScaling.Unspecified,
            ScanlineOrdering = DisplayModeScanlineOrder.Unspecified
        };

        var swapChainDescription = new SwapChainDescription
        {
            BufferCount = 2,
            Flags = SwapChainFlags.None,
            IsWindowed = true,
            ModeDescription = modeDesc,
            SampleDescription = new SharpDX.DXGI.SampleDescription(1, 0),
            OutputHandle = windowHandle,
            Usage = Usage.ShaderInput | Usage.RenderTargetOutput,
            SwapEffect = SwapEffect.Sequential
        };

        var factory = (Factory)AccessTools.Field(PlatformRenderType, "m_factory").GetValue(null) ?? new Factory1();
        AccessTools.Field(PlatformRenderType, "m_factory").SetValue(null, factory);

        var swapChain = new SwapChain(factory, deviceInstance, swapChainDescription);
        swapChainField.SetValue(null, swapChain);

        return false;
    }
}

[HarmonyPatch("VRage.Platform.Windows.Render.MyPlatformRender", "ApplySettings")]
[HarmonyPatchCategory("Finish")]
static class ApplySettingsPatch
{
    static bool Prefix(MyRenderDeviceSettings? settings)
    {
        if (settings.HasValue)
        {
            AccessTools.Field(
                AccessTools.TypeByName("VRage.Platform.Windows.Render.MyPlatformRender"), "m_settings")
                .SetValue(null, settings.Value);
        }
        return false;
    }
}

[HarmonyPatch("VRage.Platform.Windows.Render.MyWindowsRender", "CreateRenderAnnotation")]
[HarmonyPatchCategory("Finish")]
static class CreateRenderAnnotationPatch
{
    static bool Prefix(ref object __result)
    {
        __result = null;
        return false;
    }
}
