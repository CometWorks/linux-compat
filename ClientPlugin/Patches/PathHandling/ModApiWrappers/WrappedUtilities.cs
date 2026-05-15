using System;
using System.IO;
using System.Text;
using Sandbox.ModAPI.Interfaces;
using VRage.FileSystem;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Private;
using VRage.Utils;

namespace ClientPlugin.Patches.PathHandling.ModApiWrappers;

/// <summary>
/// Mod-facing wrapper for <see cref="IMyUtilities"/>. Handles three classes
/// of translation:
///
/// <list type="number">
///   <item>
///     <c>GamePaths</c> and <c>ConfigDedicated</c> are wrapped on access so
///     the chain <c>MyAPIGateway.Utilities.GamePaths.ContentPath</c> hits a
///     translated getter.
///   </item>
///   <item>
///     <c>ReadFile*</c> / <c>FileExists*</c> for mod-location and game-content
///     are reimplemented (separator normalisation + case-insensitive resolve
///     against the on-disk casing) — Linux's case-sensitive FS would otherwise
///     reject the typical Windows-baked casing in mod source.
///   </item>
///   <item>
///     The 18 storage methods (<c>*FileIn{Global,Local,World}Storage</c>)
///     scrub the caller-supplied filename through the engine's own fixed
///     invalid-char list. Many mods filter with <c>Path.GetInvalidFileNameChars()</c>
///     which on Linux returns only <c>{'\0','/'}</c> — so colons, asterisks,
///     etc. survive the mod's scrub and trip the engine's
///     <c>IndexOfAny(MyKeenUtils.GetFixedInvalidFileNameChars())</c> guard.
///     The replacement char <c>'_'</c> matches the convention used by
///     CoreParts-style mod templates.
///   </item>
/// </list>
///
/// Engine code reaches the unwrapped <c>MyAPIUtilities.Static</c> directly
/// (via concrete-typed references inside <c>Sandbox.Game</c>); only mod
/// scripts route through <c>MyAPIGateway.Utilities</c>, which is what the
/// install patch reassigns.
/// </summary>
internal sealed class WrappedUtilities : IMyUtilities
{
    private const string Tag = "[LinuxCompat][Storage]";
    private const char ScrubReplacement = '_';

    private readonly IMyUtilities _inner;
    private readonly WrappedGamePaths _gamePaths;
    private WrappedConfigDedicated _configDedicated;
    private IMyConfigDedicated _configDedicatedSource;

    public WrappedUtilities(IMyUtilities inner)
    {
        _inner = inner;
        _gamePaths = new WrappedGamePaths(inner.GamePaths);
    }

    // ---- Translated property accessors ----------------------------------

    public IMyGamePaths GamePaths => _gamePaths;

    public IMyConfigDedicated ConfigDedicated
    {
        get
        {
            // ConfigDedicated reads MySandboxGame.ConfigDedicated which is a
            // static field that can be null or be replaced. Memoise as long
            // as the source instance hasn't changed.
            var src = _inner.ConfigDedicated;
            if (src == null)
                return null;
            if (!ReferenceEquals(src, _configDedicatedSource))
            {
                _configDedicatedSource = src;
                _configDedicated = new WrappedConfigDedicated(src);
            }
            return _configDedicated;
        }
    }

    public bool IsDedicated => _inner.IsDedicated;

    // ---- Reimplemented file methods (mod-relative & game-content) -------

    public TextReader ReadFileInModLocation(string file, MyObjectBuilder_Checkpoint.ModItem modItem)
    {
        if (file.IndexOfAny(MyKeenUtils.GetFixedInvalidPathChars()) != -1)
            throw new FileNotFoundException();

        file = PathHelpers.Normalize(file);
        var modPath = modItem.GetPath();
        var fullPath = Path.GetFullPath(Path.Combine(modPath, file));
        if (fullPath.StartsWith(modPath))
        {
            var protectedDir = Path.Combine(modPath, "Data", "Scripts");
            if (fullPath.StartsWith(protectedDir))
                throw new FileNotFoundException("Access to protected location '" + protectedDir + "' not allowed.", fullPath);

            var resolved = CaseInsensitivePathResolver.Resolve(file, modPath);
            var stream = MyFileSystem.OpenRead(resolved);
            if (stream != null)
                return new StreamReader(stream);
        }
        throw new FileNotFoundException();
    }

    public TextReader ReadFileInGameContent(string file)
    {
        if (file.IndexOfAny(MyKeenUtils.GetFixedInvalidPathChars()) != -1)
            throw new FileNotFoundException();

        file = PathHelpers.Normalize(file);
        var resolved = PathHelpers.ResolveContentFilePath(file, MyFileSystem.ContentPath);
        if (resolved.StartsWith(MyFileSystem.ContentPath))
        {
            var stream = MyFileSystem.OpenRead(resolved);
            if (stream != null)
                return new StreamReader(stream);
        }
        throw new FileNotFoundException();
    }

    public bool FileExistsInModLocation(string file, MyObjectBuilder_Checkpoint.ModItem modItem)
    {
        if (file.IndexOfAny(MyKeenUtils.GetFixedInvalidPathChars()) != -1)
            return false;

        file = PathHelpers.Normalize(file);
        var modPath = modItem.GetPath();
        var fullPath = Path.GetFullPath(Path.Combine(modPath, file));
        if (!fullPath.StartsWith(modPath))
            return false;

        var protectedDir = Path.Combine(modPath, "Data", "Scripts");
        if (fullPath.StartsWith(protectedDir))
            return false;

        var resolved = CaseInsensitivePathResolver.Resolve(file, modPath);
        return File.Exists(resolved);
    }

    public bool FileExistsInGameContent(string file)
    {
        if (file.IndexOfAny(MyKeenUtils.GetFixedInvalidPathChars()) != -1)
            return false;

        file = PathHelpers.Normalize(file);
        var resolved = PathHelpers.ResolveContentFilePath(file, MyFileSystem.ContentPath);
        return resolved.StartsWith(MyFileSystem.ContentPath) && File.Exists(resolved);
    }

    public BinaryReader ReadBinaryFileInModLocation(string file, MyObjectBuilder_Checkpoint.ModItem modItem)
        => _inner.ReadBinaryFileInModLocation(PathHelpers.Normalize(file), modItem);

    public BinaryReader ReadBinaryFileInGameContent(string file)
        => _inner.ReadBinaryFileInGameContent(PathHelpers.Normalize(file));

    // ---- Storage family (scrub + forward, log on engine-side reject) ----
    //
    // The 18 storage methods all share the same Windows-fixed
    // invalid-filename guard inside MyAPIUtilities. Scrubbing here turns a
    // ':' or '*' in a mod-supplied filename into '_' so the engine accepts
    // it. If a future game update changes the guard so the scrub no longer
    // suffices, the LogIfThrew sentinel surfaces it without altering the
    // exception.

    public bool FileExistsInLocalStorage(string file, Type callingType)
        => InvokeStorage("FileExistsInLocalStorage", ref file, callingType,
            (f, t) => _inner.FileExistsInLocalStorage(f, t));

    public bool FileExistsInWorldStorage(string file, Type callingType)
        => InvokeStorage("FileExistsInWorldStorage", ref file, callingType,
            (f, t) => _inner.FileExistsInWorldStorage(f, t));

    public bool FileExistsInGlobalStorage(string file)
        => InvokeStorageNoType("FileExistsInGlobalStorage", ref file,
            f => _inner.FileExistsInGlobalStorage(f));

    public void DeleteFileInLocalStorage(string file, Type callingType)
        => InvokeStorageVoid("DeleteFileInLocalStorage", ref file, callingType,
            (f, t) => _inner.DeleteFileInLocalStorage(f, t));

    public void DeleteFileInWorldStorage(string file, Type callingType)
        => InvokeStorageVoid("DeleteFileInWorldStorage", ref file, callingType,
            (f, t) => _inner.DeleteFileInWorldStorage(f, t));

    public void DeleteFileInGlobalStorage(string file)
        => InvokeStorageVoidNoType("DeleteFileInGlobalStorage", ref file,
            f => _inner.DeleteFileInGlobalStorage(f));

    public TextReader ReadFileInLocalStorage(string file, Type callingType)
        => InvokeStorage("ReadFileInLocalStorage", ref file, callingType,
            (f, t) => _inner.ReadFileInLocalStorage(f, t));

    public TextReader ReadFileInWorldStorage(string file, Type callingType)
        => InvokeStorage("ReadFileInWorldStorage", ref file, callingType,
            (f, t) => _inner.ReadFileInWorldStorage(f, t));

    public TextReader ReadFileInGlobalStorage(string file)
        => InvokeStorageNoType("ReadFileInGlobalStorage", ref file,
            f => _inner.ReadFileInGlobalStorage(f));

    public TextWriter WriteFileInLocalStorage(string file, Type callingType)
        => InvokeStorage("WriteFileInLocalStorage", ref file, callingType,
            (f, t) => _inner.WriteFileInLocalStorage(f, t));

    public TextWriter WriteFileInWorldStorage(string file, Type callingType)
        => InvokeStorage("WriteFileInWorldStorage", ref file, callingType,
            (f, t) => _inner.WriteFileInWorldStorage(f, t));

    public TextWriter WriteFileInGlobalStorage(string file)
        => InvokeStorageNoType("WriteFileInGlobalStorage", ref file,
            f => _inner.WriteFileInGlobalStorage(f));

    public BinaryReader ReadBinaryFileInLocalStorage(string file, Type callingType)
        => InvokeStorage("ReadBinaryFileInLocalStorage", ref file, callingType,
            (f, t) => _inner.ReadBinaryFileInLocalStorage(f, t));

    public BinaryReader ReadBinaryFileInWorldStorage(string file, Type callingType)
        => InvokeStorage("ReadBinaryFileInWorldStorage", ref file, callingType,
            (f, t) => _inner.ReadBinaryFileInWorldStorage(f, t));

    public BinaryReader ReadBinaryFileInGlobalStorage(string file)
        => InvokeStorageNoType("ReadBinaryFileInGlobalStorage", ref file,
            f => _inner.ReadBinaryFileInGlobalStorage(f));

    public BinaryWriter WriteBinaryFileInLocalStorage(string file, Type callingType)
        => InvokeStorage("WriteBinaryFileInLocalStorage", ref file, callingType,
            (f, t) => _inner.WriteBinaryFileInLocalStorage(f, t));

    public BinaryWriter WriteBinaryFileInWorldStorage(string file, Type callingType)
        => InvokeStorage("WriteBinaryFileInWorldStorage", ref file, callingType,
            (f, t) => _inner.WriteBinaryFileInWorldStorage(f, t));

    public BinaryWriter WriteBinaryFileInGlobalStorage(string file)
        => InvokeStorageNoType("WriteBinaryFileInGlobalStorage", ref file,
            f => _inner.WriteBinaryFileInGlobalStorage(f));

    // ---- Untranslated forwarders ----------------------------------------

    public event MessageEnteredDel MessageEntered
    {
        add => _inner.MessageEntered += value;
        remove => _inner.MessageEntered -= value;
    }

    public event MessageEnteredSenderDel MessageEnteredSender
    {
        add => _inner.MessageEnteredSender += value;
        remove => _inner.MessageEnteredSender -= value;
    }

    public event Action<ulong, string> MessageRecieved
    {
        add => _inner.MessageRecieved += value;
        remove => _inner.MessageRecieved -= value;
    }

    public string GetTypeName(Type type) => _inner.GetTypeName(type);

    public void ShowNotification(string message, int disappearTimeMs = 2000, string font = "White")
        => _inner.ShowNotification(message, disappearTimeMs, font);

    public IMyHudNotification CreateNotification(string message, int disappearTimeMs = 2000, string font = "White")
        => _inner.CreateNotification(message, disappearTimeMs, font);

    public void ShowMessage(string sender, string messageText) => _inner.ShowMessage(sender, messageText);
    public void SendMessage(string messageText) => _inner.SendMessage(messageText);

    public string SerializeToXML<T>(T objToSerialize) => _inner.SerializeToXML(objToSerialize);
    public T SerializeFromXML<T>(string buffer) => _inner.SerializeFromXML<T>(buffer);
    public byte[] SerializeToBinary<T>(T obj) => _inner.SerializeToBinary(obj);
    public T SerializeFromBinary<T>(byte[] data) => _inner.SerializeFromBinary<T>(data);

    public void InvokeOnGameThread(Action action, string invokerName = "ModAPI", int StartAt = -1, int RepeatTimes = 0)
        => _inner.InvokeOnGameThread(action, invokerName, StartAt, RepeatTimes);

    public void ShowMissionScreen(string screenTitle = null, string currentObjectivePrefix = null,
        string currentObjective = null, string screenDescription = null,
        Action<ResultEnum> callback = null, string okButtonCaption = null)
        => _inner.ShowMissionScreen(screenTitle, currentObjectivePrefix, currentObjective,
            screenDescription, callback, okButtonCaption);

    public IMyHudObjectiveLine GetObjectiveLine() => _inner.GetObjectiveLine();

    public void SetVariable<T>(string name, T value) => _inner.SetVariable(name, value);
    public bool GetVariable<T>(string name, out T value) => _inner.GetVariable(name, out value);
    public bool RemoveVariable(string name) => _inner.RemoveVariable(name);

    public void RegisterMessageHandler(long id, Action<object> messageHandler)
        => _inner.RegisterMessageHandler(id, messageHandler);

    public void UnregisterMessageHandler(long id, Action<object> messageHandler)
        => _inner.UnregisterMessageHandler(id, messageHandler);

    public void SendModMessage(long id, object payload) => _inner.SendModMessage(id, payload);

    // ---- Storage helpers ------------------------------------------------

    private TResult InvokeStorage<TResult>(string method, ref string file, Type callingType,
        Func<string, Type, TResult> call)
    {
        file = Scrub(file);
        try { return call(file, callingType); }
        catch (Exception ex) { LogIfThrew(method, file, callingType, ex); throw; }
    }

    private void InvokeStorageVoid(string method, ref string file, Type callingType,
        Action<string, Type> call)
    {
        file = Scrub(file);
        try { call(file, callingType); }
        catch (Exception ex) { LogIfThrew(method, file, callingType, ex); throw; }
    }

    private TResult InvokeStorageNoType<TResult>(string method, ref string file,
        Func<string, TResult> call)
    {
        file = Scrub(file);
        try { return call(file); }
        catch (Exception ex) { LogIfThrew(method, file, null, ex); throw; }
    }

    private void InvokeStorageVoidNoType(string method, ref string file,
        Action<string> call)
    {
        file = Scrub(file);
        try { call(file); }
        catch (Exception ex) { LogIfThrew(method, file, null, ex); throw; }
    }

    private static string Scrub(string file)
    {
        if (string.IsNullOrEmpty(file))
            return file;

        var invalid = MyKeenUtils.GetFixedInvalidFileNameChars();
        if (file.IndexOfAny(invalid) < 0)
            return file;

        var sb = new StringBuilder(file.Length);
        foreach (var c in file)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? ScrubReplacement : c);
        return sb.ToString();
    }

    private static void LogIfThrew(string method, string file, Type callingType, Exception ex)
    {
        try
        {
            MyLog.Default?.WriteLine(
                $"{Tag} {method} threw {ex.GetType().FullName} after scrub: " +
                $"{ex.Message} (file='{file ?? "<null>"}', " +
                $"callingType='{callingType?.FullName ?? "<null>"}')");
        }
        catch
        {
            // Sentinel only; never swallow or alter the exception.
        }
    }
}
