using System;
using System.IO;
using System.Xml;
using System.Xml.Serialization;
using Sandbox.Game.World;
using Sandbox.ModAPI.Interfaces;
using VRage.FileSystem;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Private;
using VRage.Utils;

namespace ClientPlugin.Patches.PathHandling.ModApiWrappers;

/// <summary>
/// Emulates Windows file API behavior for every MyAPIGateway.Utilities caller:
/// file lookups (mod location, game content, and storage) resolve Linux
/// filesystem casing, and XML serialization uses CRLF. Filenames Windows
/// rejects keep the engine's own Windows exception shape (Write/Read throw
/// FileNotFoundException, FileExists returns false, Delete no-ops) because
/// the engine validates against the same fixed character list on all
/// platforms. Path translation is NOT done here — it is injected
/// into mod code by the compilation rewriter, so plugins calling this API keep
/// native paths. Engine code continues to use the unwrapped concrete API.
/// </summary>
internal sealed class WrappedUtilities : IMyUtilities
{
    private const string Tag = "[LinuxCompat][Storage]";

    // Canonical .NET Framework ordering for Windows-invalid filename characters.
    private static readonly char[] WindowsInvalidFileNameChars =
    {
        '"',
        '<',
        '>',
        '|',
        '\0',
        (char)1,
        (char)2,
        (char)3,
        (char)4,
        (char)5,
        (char)6,
        (char)7,
        (char)8,
        (char)9,
        (char)10,
        (char)11,
        (char)12,
        (char)13,
        (char)14,
        (char)15,
        (char)16,
        (char)17,
        (char)18,
        (char)19,
        (char)20,
        (char)21,
        (char)22,
        (char)23,
        (char)24,
        (char)25,
        (char)26,
        (char)27,
        (char)28,
        (char)29,
        (char)30,
        (char)31,
        ':',
        '*',
        '?',
        '\\',
        '/',
    };

    private readonly IMyUtilities _inner;

    public WrappedUtilities(IMyUtilities inner)
    {
        _inner = inner;
    }

    // Path mapping happens in rewritten mod code, so these stay native.
    public IMyGamePaths GamePaths => _inner.GamePaths;

    public IMyConfigDedicated ConfigDedicated => _inner.ConfigDedicated;

    public bool IsDedicated => _inner.IsDedicated;

    public TextReader ReadFileInModLocation(string file, MyObjectBuilder_Checkpoint.ModItem modItem)
    {
        if (file.IndexOfAny(MyKeenUtils.GetFixedInvalidPathChars()) != -1)
            throw new FileNotFoundException();

        file = PathHelpers.FromWindowsPath(file);
        var modPath = modItem.GetPath();
        var fullPath = Path.GetFullPath(Path.Combine(modPath, file));
        if (fullPath.StartsWith(modPath))
        {
            var protectedDir = Path.Combine(modPath, "Data", "Scripts");
            if (fullPath.StartsWith(protectedDir))
                throw new FileNotFoundException(
                    "Access to protected location '" + protectedDir + "' not allowed.",
                    fullPath
                );

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

        file = PathHelpers.FromWindowsPath(file);
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

        file = PathHelpers.FromWindowsPath(file);
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

        file = PathHelpers.FromWindowsPath(file);
        var resolved = PathHelpers.ResolveContentFilePath(file, MyFileSystem.ContentPath);
        return resolved.StartsWith(MyFileSystem.ContentPath) && File.Exists(resolved);
    }

    public BinaryReader ReadBinaryFileInModLocation(
        string file,
        MyObjectBuilder_Checkpoint.ModItem modItem
    )
    {
        // Mirrors ReadFileInModLocation: resolve casing here instead of
        // forwarding to the engine, which requires exact on-disk casing.
        if (file.IndexOfAny(MyKeenUtils.GetFixedInvalidPathChars()) != -1)
            throw new FileNotFoundException();

        file = PathHelpers.FromWindowsPath(file);
        var modPath = modItem.GetPath();
        var fullPath = Path.GetFullPath(Path.Combine(modPath, file));
        if (fullPath.StartsWith(modPath))
        {
            var protectedDir = Path.Combine(modPath, "Data", "Scripts");
            if (fullPath.StartsWith(protectedDir))
                throw new FileNotFoundException(
                    "Access to protected location '" + protectedDir + "' not allowed.",
                    fullPath
                );

            var resolved = CaseInsensitivePathResolver.Resolve(file, modPath);
            var stream = MyFileSystem.OpenRead(resolved);
            if (stream != null)
                return new BinaryReader(stream);
        }
        throw new FileNotFoundException();
    }

    public BinaryReader ReadBinaryFileInGameContent(string file)
    {
        // Mirrors ReadFileInGameContent for the same casing reasons.
        if (file.IndexOfAny(MyKeenUtils.GetFixedInvalidPathChars()) != -1)
            throw new FileNotFoundException();

        file = PathHelpers.FromWindowsPath(file);
        var resolved = PathHelpers.ResolveContentFilePath(file, MyFileSystem.ContentPath);
        if (resolved.StartsWith(MyFileSystem.ContentPath))
        {
            var stream = MyFileSystem.OpenRead(resolved);
            if (stream != null)
                return new BinaryReader(stream);
        }
        throw new FileNotFoundException();
    }

    // Storage filenames resolve Linux filesystem casing before delegation;
    // the engine's own fixed invalid-character checks already produce the
    // Windows exception shape (Write/Read throw, Exists is false, Delete
    // no-ops), so no extra validation happens here.

    public bool FileExistsInLocalStorage(string file, Type callingType) =>
        InvokeStorage(
            "FileExistsInLocalStorage",
            file,
            callingType,
            (f, t) => _inner.FileExistsInLocalStorage(f, t)
        );

    public bool FileExistsInWorldStorage(string file, Type callingType) =>
        InvokeStorage(
            "FileExistsInWorldStorage",
            file,
            callingType,
            (f, t) => _inner.FileExistsInWorldStorage(f, t)
        );

    public bool FileExistsInGlobalStorage(string file) =>
        InvokeStorageNoType(
            "FileExistsInGlobalStorage",
            file,
            f => _inner.FileExistsInGlobalStorage(f)
        );

    public void DeleteFileInLocalStorage(string file, Type callingType) =>
        InvokeStorageVoid(
            "DeleteFileInLocalStorage",
            file,
            callingType,
            (f, t) => _inner.DeleteFileInLocalStorage(f, t)
        );

    public void DeleteFileInWorldStorage(string file, Type callingType) =>
        InvokeStorageVoid(
            "DeleteFileInWorldStorage",
            file,
            callingType,
            (f, t) => _inner.DeleteFileInWorldStorage(f, t)
        );

    public void DeleteFileInGlobalStorage(string file) =>
        InvokeStorageVoidNoType(
            "DeleteFileInGlobalStorage",
            file,
            f => _inner.DeleteFileInGlobalStorage(f)
        );

    public TextReader ReadFileInLocalStorage(string file, Type callingType) =>
        InvokeStorage(
            "ReadFileInLocalStorage",
            file,
            callingType,
            (f, t) => _inner.ReadFileInLocalStorage(f, t)
        );

    public TextReader ReadFileInWorldStorage(string file, Type callingType) =>
        InvokeStorage(
            "ReadFileInWorldStorage",
            file,
            callingType,
            (f, t) => _inner.ReadFileInWorldStorage(f, t)
        );

    public TextReader ReadFileInGlobalStorage(string file) =>
        InvokeStorageNoType(
            "ReadFileInGlobalStorage",
            file,
            f => _inner.ReadFileInGlobalStorage(f)
        );

    public TextWriter WriteFileInLocalStorage(string file, Type callingType) =>
        InvokeStorage(
            "WriteFileInLocalStorage",
            file,
            callingType,
            (f, t) => _inner.WriteFileInLocalStorage(f, t)
        );

    public TextWriter WriteFileInWorldStorage(string file, Type callingType) =>
        InvokeStorage(
            "WriteFileInWorldStorage",
            file,
            callingType,
            (f, t) => _inner.WriteFileInWorldStorage(f, t)
        );

    public TextWriter WriteFileInGlobalStorage(string file) =>
        InvokeStorageNoType(
            "WriteFileInGlobalStorage",
            file,
            f => _inner.WriteFileInGlobalStorage(f)
        );

    public BinaryReader ReadBinaryFileInLocalStorage(string file, Type callingType) =>
        InvokeStorage(
            "ReadBinaryFileInLocalStorage",
            file,
            callingType,
            (f, t) => _inner.ReadBinaryFileInLocalStorage(f, t)
        );

    public BinaryReader ReadBinaryFileInWorldStorage(string file, Type callingType) =>
        InvokeStorage(
            "ReadBinaryFileInWorldStorage",
            file,
            callingType,
            (f, t) => _inner.ReadBinaryFileInWorldStorage(f, t)
        );

    public BinaryReader ReadBinaryFileInGlobalStorage(string file) =>
        InvokeStorageNoType(
            "ReadBinaryFileInGlobalStorage",
            file,
            f => _inner.ReadBinaryFileInGlobalStorage(f)
        );

    public BinaryWriter WriteBinaryFileInLocalStorage(string file, Type callingType) =>
        InvokeStorage(
            "WriteBinaryFileInLocalStorage",
            file,
            callingType,
            (f, t) => _inner.WriteBinaryFileInLocalStorage(f, t)
        );

    public BinaryWriter WriteBinaryFileInWorldStorage(string file, Type callingType) =>
        InvokeStorage(
            "WriteBinaryFileInWorldStorage",
            file,
            callingType,
            (f, t) => _inner.WriteBinaryFileInWorldStorage(f, t)
        );

    public BinaryWriter WriteBinaryFileInGlobalStorage(string file) =>
        InvokeStorageNoType(
            "WriteBinaryFileInGlobalStorage",
            file,
            f => _inner.WriteBinaryFileInGlobalStorage(f)
        );

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

    public void ShowNotification(
        string message,
        int disappearTimeMs = 2000,
        string font = "White"
    ) => _inner.ShowNotification(message, disappearTimeMs, font);

    public IMyHudNotification CreateNotification(
        string message,
        int disappearTimeMs = 2000,
        string font = "White"
    ) => _inner.CreateNotification(message, disappearTimeMs, font);

    public void ShowMessage(string sender, string messageText) =>
        _inner.ShowMessage(sender, messageText);

    public void SendMessage(string messageText) => _inner.SendMessage(messageText);

    /// <summary>
    /// Uses CRLF while retaining StringWriter's UTF-16 XML declaration.
    /// </summary>
    public string SerializeToXML<T>(T objToSerialize)
    {
        // Runtime type selection preserves boxed primitive root element names.
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

    public void InvokeOnGameThread(
        Action action,
        string invokerName = "ModAPI",
        int StartAt = -1,
        int RepeatTimes = 0
    ) => _inner.InvokeOnGameThread(action, invokerName, StartAt, RepeatTimes);

    public void ShowMissionScreen(
        string screenTitle = null,
        string currentObjectivePrefix = null,
        string currentObjective = null,
        string screenDescription = null,
        Action<ResultEnum> callback = null,
        string okButtonCaption = null
    ) =>
        _inner.ShowMissionScreen(
            screenTitle,
            currentObjectivePrefix,
            currentObjective,
            screenDescription,
            callback,
            okButtonCaption
        );

    public IMyHudObjectiveLine GetObjectiveLine() => _inner.GetObjectiveLine();

    public void SetVariable<T>(string name, T value) => _inner.SetVariable(name, value);

    public bool GetVariable<T>(string name, out T value) => _inner.GetVariable(name, out value);

    public bool RemoveVariable(string name) => _inner.RemoveVariable(name);

    public void RegisterMessageHandler(long id, Action<object> messageHandler) =>
        _inner.RegisterMessageHandler(id, messageHandler);

    public void UnregisterMessageHandler(long id, Action<object> messageHandler) =>
        _inner.UnregisterMessageHandler(id, messageHandler);

    public void SendModMessage(long id, object payload) => _inner.SendModMessage(id, payload);

    private TResult InvokeStorage<TResult>(
        string method,
        string file,
        Type callingType,
        Func<string, Type, TResult> call
    )
    {
        file = ResolveStorageCase(method, file, callingType);
        try
        {
            return call(file, callingType);
        }
        catch (Exception ex)
        {
            LogIfThrew(method, file, callingType, ex);
            throw;
        }
    }

    private void InvokeStorageVoid(
        string method,
        string file,
        Type callingType,
        Action<string, Type> call
    )
    {
        file = ResolveStorageCase(method, file, callingType);
        try
        {
            call(file, callingType);
        }
        catch (Exception ex)
        {
            LogIfThrew(method, file, callingType, ex);
            throw;
        }
    }

    private TResult InvokeStorageNoType<TResult>(
        string method,
        string file,
        Func<string, TResult> call
    )
    {
        file = ResolveStorageCase(method, file, null);
        try
        {
            return call(file);
        }
        catch (Exception ex)
        {
            LogIfThrew(method, file, null, ex);
            throw;
        }
    }

    private void InvokeStorageVoidNoType(string method, string file, Action<string> call)
    {
        file = ResolveStorageCase(method, file, null);
        try
        {
            call(file);
        }
        catch (Exception ex)
        {
            LogIfThrew(method, file, null, ex);
            throw;
        }
    }

    /// <summary>
    /// Windows storage lookups are case-insensitive: when the exact filename
    /// is missing from the target storage directory, substitute an existing
    /// file that differs only in case. Names the engine rejects (separators,
    /// wildcards, control characters) pass through unchanged so its fixed
    /// invalid-character checks keep their Windows exception shape.
    /// </summary>
    private static string ResolveStorageCase(string method, string file, Type callingType)
    {
        if (string.IsNullOrEmpty(file))
            return file;
        if (file.IndexOfAny(WindowsInvalidFileNameChars) >= 0)
            return file;

        string dir = StorageDirectory(method, callingType);
        if (dir == null || File.Exists(Path.Combine(dir, file)) || !Directory.Exists(dir))
            return file;

        foreach (var candidate in Directory.EnumerateFiles(dir))
        {
            var name = Path.GetFileName(candidate);
            if (string.Equals(name, file, StringComparison.OrdinalIgnoreCase))
                return name;
        }
        return file;
    }

    /// <summary>
    /// The storage directory the engine derives for the given member; the
    /// domain is encoded in the member name (Local/World/Global suffix).
    /// </summary>
    private static string StorageDirectory(string method, Type callingType)
    {
        try
        {
            if (method.EndsWith("InGlobalStorage"))
                return Path.Combine(MyFileSystem.UserDataPath, "Storage");
            if (method.EndsWith("InLocalStorage"))
                return Path.Combine(
                    MyFileSystem.UserDataPath,
                    "Storage",
                    StorageScopeName(callingType)
                );
            if (method.EndsWith("InWorldStorage"))
            {
                var savePath = MySession.Static?.WorldSavePath;
                return savePath == null
                    ? null
                    : Path.Combine(savePath, "Storage", StorageScopeName(callingType));
            }
        }
        catch
        {
            // Unresolvable directory: skip resolution, keep engine behavior.
        }
        return null;
    }

    /// <summary>Matches the engine's per-assembly storage scope (module scope name without a .dll extension).</summary>
    private static string StorageScopeName(Type callingType)
    {
        var name = callingType.Assembly.ManifestModule.ScopeName;
        const string ext = ".dll";
        if (name.EndsWith(ext, StringComparison.InvariantCultureIgnoreCase))
            name = name.Substring(0, name.Length - ext.Length);
        return name;
    }

    private static void LogIfThrew(string method, string file, Type callingType, Exception ex)
    {
        try
        {
            MyLog.Default?.WriteLine(
                $"{Tag} {method} threw {ex.GetType().FullName} after scrub: "
                    + $"{ex.Message} (file='{file ?? "<null>"}', "
                    + $"callingType='{callingType?.FullName ?? "<null>"}')"
            );
        }
        catch
        {
            // Sentinel only; never swallow or alter the exception.
        }
    }
}
