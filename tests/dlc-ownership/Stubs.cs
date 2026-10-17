namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch(params object[] _) : Attribute;

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatchCategory(string _) : Attribute;
}

namespace VRage.Steam
{
    public sealed class MySteamService
    {
        public bool IsDlcInstalled(uint dlcId) => false;

        public bool IsDlcSupported(uint dlcId) => false;
    }
}

namespace VRage.Utils
{
    public sealed class MyLog
    {
        public static MyLog Default { get; } = new();
        public List<string> Lines { get; } = new();

        public void WriteLine(string message) => Lines.Add(message);
    }
}

namespace Steamworks
{
    public struct AppId_t(uint id)
    {
        public uint m_AppId = id;

        public static explicit operator AppId_t(uint value) => new(value);

        public static explicit operator uint(AppId_t that) => that.m_AppId;
    }

    // Fake Steam answers, set by each test.
    public record FakeDlc(uint Id, bool Available, bool Subscribed, bool Installed, string Name);

    public static class SteamApps
    {
        public static List<FakeDlc> Dlcs = new();
        public static bool Throw;
        public static int? CountOverride;

        static FakeDlc Find(AppId_t id) =>
            Throw
                ? throw new InvalidOperationException("Steamworks is not initialized.")
                : Dlcs.FirstOrDefault(d => d.Id == id.m_AppId)
                    ?? new FakeDlc(id.m_AppId, false, false, false, "");

        public static bool BIsSubscribedApp(AppId_t id) => Find(id).Subscribed;

        public static bool BIsDlcInstalled(AppId_t id) => Find(id).Installed;

        public static int GetDLCCount() =>
            Throw
                ? throw new InvalidOperationException("Steamworks is not initialized.")
                : CountOverride ?? Dlcs.Count;

        public static bool BGetDLCDataByIndex(
            int index,
            out AppId_t appId,
            out bool available,
            out string name,
            int nameBufferSize
        )
        {
            var dlc = Dlcs[index];
            appId = (AppId_t)dlc.Id;
            available = dlc.Available;
            name = dlc.Name;
            return true;
        }
    }
}
