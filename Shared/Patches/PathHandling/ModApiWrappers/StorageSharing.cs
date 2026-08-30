using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using VRage.Utils;

namespace ClientPlugin.Patches.PathHandling.ModApiWrappers;

/// <summary>
/// Emulates the Windows file sharing semantics of the storage Mod API.
///
/// The engine opens storage files as <c>FileAccess.Write | FileShare.Read</c>
/// for writing and <c>FileAccess.Read | FileShare.Read</c> for reading
/// (MyFileSystem.OpenWrite/OpenRead). On Windows the reader's share mode does
/// not admit the writer's Write access, so while a writer handle is open every
/// further open of the same file fails with IOException. Linux has no
/// mandatory sharing, so the same read succeeds and yields whatever is on disk
/// — commonly zero bytes, which SerializeFromXML silently turns into null.
///
/// Mods hit this whenever they write a default config on a worker thread and
/// read it back from the main thread. The window is theirs, but its outcome
/// must not differ per platform, so this tracks the paths of writers handed
/// out through the Mod API and makes overlapping opens throw the way Windows
/// does. Mods cannot bypass it: the script whitelist grants Stream, TextWriter,
/// TextReader, BinaryReader and BinaryWriter but not File, FileStream or
/// Directory, so all mod file access goes through IMyUtilities.
/// </summary>
internal static class StorageSharing
{
    private const string Tag = "[LinuxCompat][Storage]";

    // Sharing violations are reported with the Windows message and HRESULT
    // (0x80070020, ERROR_SHARING_VIOLATION) so callers that inspect either see
    // what they would see on Windows.
    private const int SharingViolationHResult = unchecked((int)0x80070020);

    // Full paths of storage files with a writer currently handed out.
    // Windows compares paths case-insensitively, and WrappedUtilities already
    // resolves the on-disk casing, so the comparer keeps the two consistent
    // when a mod writes and reads the same name in different casing.
    private static readonly ConcurrentDictionary<string, Lease> OpenWriters = new(
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>
    /// Registers a writer about to be opened for the given path. Throws the
    /// Windows sharing violation when another writer is already open, which is
    /// what a second FileAccess.Write open would do there.
    /// </summary>
    internal static Lease AcquireWriter(string method, string fullPath)
    {
        var lease = new Lease(fullPath);
        if (OpenWriters.TryAdd(fullPath, lease))
            return lease;

        throw SharingViolation(method, fullPath);
    }

    /// <summary>
    /// Fails a read or delete that overlaps an open writer, as Windows does.
    /// </summary>
    internal static void ThrowIfWriterOpen(string method, string fullPath)
    {
        if (OpenWriters.ContainsKey(fullPath))
            throw SharingViolation(method, fullPath);
    }

    private static IOException SharingViolation(string method, string fullPath)
    {
        var exception = new IOException(
            $"The process cannot access the file '{ReportedPath(fullPath)}' because it is being used by another process."
        )
        {
            HResult = SharingViolationHResult,
        };

        try
        {
            MyLog.Default?.WriteLine(
                $"{Tag} {method} refused: a writer for '{fullPath}' is open (Windows sharing violation)"
            );
        }
        catch
        {
            // Diagnostics only; the exception is what matters.
        }

        return exception;
    }

    /// <summary>Mods see Windows-shaped paths everywhere else, so the message keeps that shape too.</summary>
    private static string ReportedPath(string fullPath)
    {
        try
        {
            return Rewriter.WindowsPath.FromGame(fullPath);
        }
        catch
        {
            return fullPath;
        }
    }

    /// <summary>
    /// Holds a path registered as being written. Release is idempotent so the
    /// decorator's Dispose and its finalizer can both call it.
    /// </summary>
    internal sealed class Lease
    {
        private readonly string _fullPath;
        private int _released;

        internal Lease(string fullPath)
        {
            _fullPath = fullPath;
        }

        internal void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
                return;

            // Remove only our own registration: a later writer for the same
            // path owns a different lease.
            OpenWriters.TryRemove(new KeyValuePair<string, Lease>(_fullPath, this));
        }
    }

    /// <summary>
    /// Passes every write through to the writer the engine created and frees
    /// the path once the caller disposes it. The finalizer matches Windows,
    /// where a leaked writer stops blocking when its handle is finalized.
    /// </summary>
    internal sealed class TrackedTextWriter : TextWriter
    {
        private readonly TextWriter _inner;
        private readonly Lease _lease;

        internal TrackedTextWriter(TextWriter inner, Lease lease)
        {
            _inner = inner;
            _lease = lease;
        }

        ~TrackedTextWriter()
        {
            _lease.Release();
        }

        public override Encoding Encoding => _inner.Encoding;

        public override IFormatProvider FormatProvider => _inner.FormatProvider;

        public override string NewLine
        {
            get => _inner.NewLine;
            set => _inner.NewLine = value;
        }

        // The remaining Write/WriteLine overloads of TextWriter funnel into
        // these three and into WriteLine(), formatting through FormatProvider.
        public override void Write(char value) => _inner.Write(value);

        public override void Write(char[] buffer, int index, int count) =>
            _inner.Write(buffer, index, count);

        public override void Write(string value) => _inner.Write(value);

        public override void WriteLine() => _inner.WriteLine();

        public override void WriteLine(string value) => _inner.WriteLine(value);

        public override void Flush() => _inner.Flush();

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    _inner.Dispose();
                    GC.SuppressFinalize(this);
                }
            }
            finally
            {
                _lease.Release();
            }
        }
    }

    /// <summary>
    /// Binary counterpart of <see cref="TrackedTextWriter"/>. BinaryWriter
    /// writes straight to its stream, so this shares the engine writer's
    /// stream and default UTF-8 encoding and leaves closing it to that writer.
    /// </summary>
    internal sealed class TrackedBinaryWriter : BinaryWriter
    {
        private readonly BinaryWriter _inner;
        private readonly Lease _lease;

        internal TrackedBinaryWriter(BinaryWriter inner, Lease lease)
            : base(inner.BaseStream, new UTF8Encoding(false, true), leaveOpen: true)
        {
            _inner = inner;
            _lease = lease;
        }

        ~TrackedBinaryWriter()
        {
            _lease.Release();
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                // Flushes this writer without closing the stream, then hands
                // the close to the engine's writer.
                base.Dispose(disposing);
                if (disposing)
                {
                    _inner.Dispose();
                    GC.SuppressFinalize(this);
                }
            }
            finally
            {
                _lease.Release();
            }
        }
    }
}
