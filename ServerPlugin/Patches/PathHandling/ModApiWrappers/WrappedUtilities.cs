using System;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using Sandbox.ModAPI.Interfaces;
using VRage.FileSystem;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Private;
using VRage.Utils;

namespace ServerPlugin.Patches.PathHandling.ModApiWrappers;

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
///     pre-validate the caller-supplied filename against the
///     Windows-fixed invalid-filename set and throw
///     <see cref="FileNotFoundException"/> if any of
///     <c>: * ? " &lt; &gt; | \ /</c> (or chars 0..31) is present. On
///     Windows the underlying engine call already fails with
///     <c>FileNotFoundException("Unable to find the specified file.")</c>
///     for these inputs because NTFS rejects them; on Linux those chars
///     are filesystem-legal, so without this guard the file would land
///     on disk and the round-trip would silently succeed — observable
///     to mods that branch on the Windows-shape exception.
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

    // Windows-fixed invalid filename chars in canonical .NET Framework
    // order. Matches WindowsPath.InvalidFileNameChars and the Proton
    // reference diagnostic. Used purely for the storage filename guard
    // below — engine code is unaffected because this class lives behind
    // the mod-facing IMyUtilities wrapper.
    private static readonly char[] WindowsInvalidFileNameChars =
    {
        '"', '<', '>', '|', '\0',
        (char)1, (char)2, (char)3, (char)4, (char)5, (char)6, (char)7,
        (char)8, (char)9, (char)10, (char)11, (char)12, (char)13, (char)14,
        (char)15, (char)16, (char)17, (char)18, (char)19, (char)20, (char)21,
        (char)22, (char)23, (char)24, (char)25, (char)26, (char)27, (char)28,
        (char)29, (char)30, (char)31,
        ':', '*', '?', '\\', '/',
    };

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

    // ---- Storage family (Windows-shape filename guard, then forward) ---
    //
    // The 18 storage methods all share the same Windows-fixed
    // invalid-filename set. On Linux that set's chars are
    // filesystem-legal, so a mod passing "scrub:colon.txt" would
    // silently succeed instead of throwing the Windows-side
    // FileNotFoundException. ValidateFilenameOrThrow short-circuits
    // with the same exception type and default message Windows emits
    // ("Unable to find the specified file."). LogIfThrew remains in
    // place to surface engine-side rejects from otherwise-clean
    // filenames without altering the exception they raise.

    public bool FileExistsInLocalStorage(string file, Type callingType)
        => InvokeStorage("FileExistsInLocalStorage", file, callingType,
            (f, t) => _inner.FileExistsInLocalStorage(f, t));

    public bool FileExistsInWorldStorage(string file, Type callingType)
        => InvokeStorage("FileExistsInWorldStorage", file, callingType,
            (f, t) => _inner.FileExistsInWorldStorage(f, t));

    public bool FileExistsInGlobalStorage(string file)
        => InvokeStorageNoType("FileExistsInGlobalStorage", file,
            f => _inner.FileExistsInGlobalStorage(f));

    public void DeleteFileInLocalStorage(string file, Type callingType)
        => InvokeStorageVoid("DeleteFileInLocalStorage", file, callingType,
            (f, t) => _inner.DeleteFileInLocalStorage(f, t));

    public void DeleteFileInWorldStorage(string file, Type callingType)
        => InvokeStorageVoid("DeleteFileInWorldStorage", file, callingType,
            (f, t) => _inner.DeleteFileInWorldStorage(f, t));

    public void DeleteFileInGlobalStorage(string file)
        => InvokeStorageVoidNoType("DeleteFileInGlobalStorage", file,
            f => _inner.DeleteFileInGlobalStorage(f));

    public TextReader ReadFileInLocalStorage(string file, Type callingType)
        => InvokeStorage("ReadFileInLocalStorage", file, callingType,
            (f, t) => _inner.ReadFileInLocalStorage(f, t));

    public TextReader ReadFileInWorldStorage(string file, Type callingType)
        => InvokeStorage("ReadFileInWorldStorage", file, callingType,
            (f, t) => _inner.ReadFileInWorldStorage(f, t));

    public TextReader ReadFileInGlobalStorage(string file)
        => InvokeStorageNoType("ReadFileInGlobalStorage", file,
            f => _inner.ReadFileInGlobalStorage(f));

    public TextWriter WriteFileInLocalStorage(string file, Type callingType)
        => InvokeStorage("WriteFileInLocalStorage", file, callingType,
            (f, t) => _inner.WriteFileInLocalStorage(f, t));

    public TextWriter WriteFileInWorldStorage(string file, Type callingType)
        => InvokeStorage("WriteFileInWorldStorage", file, callingType,
            (f, t) => _inner.WriteFileInWorldStorage(f, t));

    public TextWriter WriteFileInGlobalStorage(string file)
        => InvokeStorageNoType("WriteFileInGlobalStorage", file,
            f => _inner.WriteFileInGlobalStorage(f));

    public BinaryReader ReadBinaryFileInLocalStorage(string file, Type callingType)
        => InvokeStorage("ReadBinaryFileInLocalStorage", file, callingType,
            (f, t) => _inner.ReadBinaryFileInLocalStorage(f, t));

    public BinaryReader ReadBinaryFileInWorldStorage(string file, Type callingType)
        => InvokeStorage("ReadBinaryFileInWorldStorage", file, callingType,
            (f, t) => _inner.ReadBinaryFileInWorldStorage(f, t));

    public BinaryReader ReadBinaryFileInGlobalStorage(string file)
        => InvokeStorageNoType("ReadBinaryFileInGlobalStorage", file,
            f => _inner.ReadBinaryFileInGlobalStorage(f));

    public BinaryWriter WriteBinaryFileInLocalStorage(string file, Type callingType)
        => InvokeStorage("WriteBinaryFileInLocalStorage", file, callingType,
            (f, t) => _inner.WriteBinaryFileInLocalStorage(f, t));

    public BinaryWriter WriteBinaryFileInWorldStorage(string file, Type callingType)
        => InvokeStorage("WriteBinaryFileInWorldStorage", file, callingType,
            (f, t) => _inner.WriteBinaryFileInWorldStorage(f, t));

    public BinaryWriter WriteBinaryFileInGlobalStorage(string file)
        => InvokeStorageNoType("WriteBinaryFileInGlobalStorage", file,
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

    /// <summary>
    /// Mirrors <c>MyAPIUtilities.SerializeToXML</c> but pins
    /// <see cref="XmlWriterSettings.NewLineChars"/> to <c>"\r\n"</c>.
    /// The engine implementation builds a plain
    /// <see cref="System.IO.StringWriter"/> and lets
    /// <see cref="XmlSerializer.Serialize(System.IO.TextWriter, object)"/>
    /// pick default writer settings — those default
    /// <c>NewLineChars</c> to <see cref="Environment.NewLine"/>, which
    /// is <c>"\n"</c> on Linux .NET 10 and changes the byte length of
    /// the result by 1 (54 → 53 in the diagnostic baseline). Driving
    /// our own <see cref="XmlWriter"/> with explicit CRLF settings
    /// makes the output byte-identical to the Windows-baked
    /// reference. <see cref="XmlWriterSettings.Encoding"/> is left
    /// unset on purpose — passing a <see cref="System.IO.StringWriter"/>
    /// forces UTF-16 in the declaration, matching the engine output
    /// on Windows.
    /// </summary>
    public string SerializeToXML<T>(T objToSerialize)
    {
        // Bind against the runtime type, like the engine does: a
        // boxed primitive serialized through this method must produce
        // the same root element name (<int>, <double>, ...) it would
        // on Windows.
        var serializer = new XmlSerializer(objToSerialize.GetType());
        var sw = new StringWriter();
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\r\n",
            NewLineHandling = NewLineHandling.Replace,
            OmitXmlDeclaration = false,
        };
        using (var xw = XmlWriter.Create(sw, settings))
            serializer.Serialize(xw, objToSerialize);
        return sw.ToString();
    }

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

    private TResult InvokeStorage<TResult>(string method, string file, Type callingType,
        Func<string, Type, TResult> call)
    {
        ValidateFilenameOrThrow(file);
        try { return call(file, callingType); }
        catch (Exception ex) { LogIfThrew(method, file, callingType, ex); throw; }
    }

    private void InvokeStorageVoid(string method, string file, Type callingType,
        Action<string, Type> call)
    {
        ValidateFilenameOrThrow(file);
        try { call(file, callingType); }
        catch (Exception ex) { LogIfThrew(method, file, callingType, ex); throw; }
    }

    private TResult InvokeStorageNoType<TResult>(string method, string file,
        Func<string, TResult> call)
    {
        ValidateFilenameOrThrow(file);
        try { return call(file); }
        catch (Exception ex) { LogIfThrew(method, file, null, ex); throw; }
    }

    private void InvokeStorageVoidNoType(string method, string file,
        Action<string> call)
    {
        ValidateFilenameOrThrow(file);
        try { call(file); }
        catch (Exception ex) { LogIfThrew(method, file, null, ex); throw; }
    }

    /// <summary>
    /// Reject filenames Windows rejects, with the same exception shape
    /// the underlying engine call raises on Windows
    /// (<see cref="FileNotFoundException"/> + default message). Linux's
    /// filesystem would otherwise accept these chars and the round-trip
    /// would silently succeed — a behavioural divergence visible to any
    /// mod that branches on the Windows-baked exception.
    /// </summary>
    private static void ValidateFilenameOrThrow(string file)
    {
        if (string.IsNullOrEmpty(file))
            return;
        if (file.IndexOfAny(WindowsInvalidFileNameChars) >= 0)
            throw new FileNotFoundException();
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
