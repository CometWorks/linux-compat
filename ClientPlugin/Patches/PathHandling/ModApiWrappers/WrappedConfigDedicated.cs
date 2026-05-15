using System.Collections.Generic;
using VRage.Game;
using VRage.Game.ModAPI;

namespace ClientPlugin.Patches.PathHandling.ModApiWrappers;

/// <summary>
/// Mod-facing wrapper for <see cref="IMyConfigDedicated"/>. Two members carry
/// filesystem paths and need bidirectional translation:
///
/// <list type="bullet">
///   <item><c>PremadeCheckpointPath</c>: getter returns Windows form to mods,
///         setter normalises Windows form back to Linux before storing (the
///         engine's <c>MySandboxGame.RunCampaign</c> reads the same backing
///         field directly).</item>
///   <item><c>GetFilePath()</c>: returns Windows form. Read-only — no
///         engine-direction translation needed.</item>
/// </list>
///
/// Everything else is straight delegation. The wrapper exists only because
/// mods reach this object through <c>MyAPIGateway.Utilities.ConfigDedicated</c>,
/// so engine-internal call sites (e.g. <c>ConfigDedicated.PremadeCheckpointPath</c>
/// inside <c>MySandboxGame</c>) bypass the wrapper entirely.
/// </summary>
internal sealed class WrappedConfigDedicated : IMyConfigDedicated
{
    private readonly IMyConfigDedicated _inner;

    public WrappedConfigDedicated(IMyConfigDedicated inner)
    {
        _inner = inner;
    }

    // ---- Translated members ---------------------------------------------

    public string PremadeCheckpointPath
    {
        get => PathHelpers.ToWindowsPath(_inner.PremadeCheckpointPath);
        // Mods that write a Windows-shaped path back into the config must
        // not leave backslashes in a field the engine consumes for file I/O.
        set => _inner.PremadeCheckpointPath = PathHelpers.Normalize(value);
    }

    public string GetFilePath() => PathHelpers.ToWindowsPath(_inner.GetFilePath());

    // ---- Forwarders -----------------------------------------------------

    public List<string> Administrators { get => _inner.Administrators; set => _inner.Administrators = value; }
    public int AsteroidAmount { get => _inner.AsteroidAmount; set => _inner.AsteroidAmount = value; }
    public List<ulong> Banned { get => _inner.Banned; set => _inner.Banned = value; }
    public List<ulong> Reserved { get => _inner.Reserved; set => _inner.Reserved = value; }
    public ulong GroupID { get => _inner.GroupID; set => _inner.GroupID = value; }
    public bool IgnoreLastSession { get => _inner.IgnoreLastSession; set => _inner.IgnoreLastSession = value; }
    public string IP { get => _inner.IP; set => _inner.IP = value; }
    public string LoadWorld { get => _inner.LoadWorld; set => _inner.LoadWorld = value; }
    public bool CrossPlatform { get => _inner.CrossPlatform; set => _inner.CrossPlatform = value; }
    public bool VerboseNetworkLogging { get => _inner.VerboseNetworkLogging; set => _inner.VerboseNetworkLogging = value; }
    public bool PauseGameWhenEmpty { get => _inner.PauseGameWhenEmpty; set => _inner.PauseGameWhenEmpty = value; }
    public string MessageOfTheDay { get => _inner.MessageOfTheDay; set => _inner.MessageOfTheDay = value; }
    public string MessageOfTheDayUrl { get => _inner.MessageOfTheDayUrl; set => _inner.MessageOfTheDayUrl = value; }
    public bool AutoRestartEnabled { get => _inner.AutoRestartEnabled; set => _inner.AutoRestartEnabled = value; }
    public int AutoRestatTimeInMin { get => _inner.AutoRestatTimeInMin; set => _inner.AutoRestatTimeInMin = value; }
    public bool AutoUpdateEnabled { get => _inner.AutoUpdateEnabled; set => _inner.AutoUpdateEnabled = value; }
    public int AutoUpdateCheckIntervalInMin { get => _inner.AutoUpdateCheckIntervalInMin; set => _inner.AutoUpdateCheckIntervalInMin = value; }
    public int AutoUpdateRestartDelayInMin { get => _inner.AutoUpdateRestartDelayInMin; set => _inner.AutoUpdateRestartDelayInMin = value; }
    public bool RestartSave { get => _inner.RestartSave; set => _inner.RestartSave = value; }
    public string AutoUpdateSteamBranch { get => _inner.AutoUpdateSteamBranch; set => _inner.AutoUpdateSteamBranch = value; }
    public string AutoUpdateBranchPassword { get => _inner.AutoUpdateBranchPassword; set => _inner.AutoUpdateBranchPassword = value; }
    public string ServerName { get => _inner.ServerName; set => _inner.ServerName = value; }
    public int ServerPort { get => _inner.ServerPort; set => _inner.ServerPort = value; }
    public MyObjectBuilder_SessionSettings SessionSettings { get => _inner.SessionSettings; set => _inner.SessionSettings = value; }
    public int SteamPort { get => _inner.SteamPort; set => _inner.SteamPort = value; }
    public string WorldName { get => _inner.WorldName; set => _inner.WorldName = value; }
    public string ServerDescription { get => _inner.ServerDescription; set => _inner.ServerDescription = value; }
    public string ServerPasswordHash { get => _inner.ServerPasswordHash; set => _inner.ServerPasswordHash = value; }
    public string ServerPasswordSalt { get => _inner.ServerPasswordSalt; set => _inner.ServerPasswordSalt = value; }
    public bool RemoteApiEnabled { get => _inner.RemoteApiEnabled; set => _inner.RemoteApiEnabled = value; }
    public string RemoteSecurityKey { get => _inner.RemoteSecurityKey; set => _inner.RemoteSecurityKey = value; }
    public int RemoteApiPort { get => _inner.RemoteApiPort; set => _inner.RemoteApiPort = value; }
    public string RemoteApiIP { get => _inner.RemoteApiIP; set => _inner.RemoteApiIP = value; }
    public List<string> Plugins { get => _inner.Plugins; set => _inner.Plugins = value; }
    public float WatcherInterval { get => _inner.WatcherInterval; set => _inner.WatcherInterval = value; }
    public float WatcherSimulationSpeedMinimum { get => _inner.WatcherSimulationSpeedMinimum; set => _inner.WatcherSimulationSpeedMinimum = value; }
    public int ManualActionDelay { get => _inner.ManualActionDelay; set => _inner.ManualActionDelay = value; }
    public string ManualActionChatMessage { get => _inner.ManualActionChatMessage; set => _inner.ManualActionChatMessage = value; }
    public bool AutodetectDependencies { get => _inner.AutodetectDependencies; set => _inner.AutodetectDependencies = value; }
    public bool SaveChatToLog { get => _inner.SaveChatToLog; set => _inner.SaveChatToLog = value; }
    public string NetworkType { get => _inner.NetworkType; set => _inner.NetworkType = value; }
    public List<string> NetworkParameters { get => _inner.NetworkParameters; set => _inner.NetworkParameters = value; }
    public bool ConsoleCompatibility { get => _inner.ConsoleCompatibility; set => _inner.ConsoleCompatibility = value; }
    public bool ChatAntiSpamEnabled { get => _inner.ChatAntiSpamEnabled; set => _inner.ChatAntiSpamEnabled = value; }
    public int SameMessageTimeout { get => _inner.SameMessageTimeout; set => _inner.SameMessageTimeout = value; }
    public float SpamMessagesTime { get => _inner.SpamMessagesTime; set => _inner.SpamMessagesTime = value; }
    public int SpamMessagesTimeout { get => _inner.SpamMessagesTimeout; set => _inner.SpamMessagesTimeout = value; }
    public long DedicatedId { get => _inner.DedicatedId; set => _inner.DedicatedId = value; }

    public void Load(string path = null) => _inner.Load(path);
    public void Save(string path = null) => _inner.Save(path);
    public void SetPassword(string password) => _inner.SetPassword(password);
    public void GenerateRemoteSecurityKey() => _inner.GenerateRemoteSecurityKey();
}
