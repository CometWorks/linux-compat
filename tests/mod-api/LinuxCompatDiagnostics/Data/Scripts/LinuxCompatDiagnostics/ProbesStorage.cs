using System;
using System.IO;
using Sandbox.ModAPI;
using VRage.Game;

namespace LinuxCompatDiagnostics
{
    /// <summary>
    /// Storage and file-read probes over IMyUtilities. Windows reference
    /// behavior (verified against decompiled MyAPIUtilities and a .NET
    /// Framework capture under Proton):
    ///   - Names containing any of \ / " &lt; &gt; | : * ? or control chars:
    ///     Write/Read throw FileNotFoundException, FileExists returns false,
    ///     Delete silently no-ops.
    ///   - Filesystem lookups are case-insensitive.
    ///   - TextWriter.WriteLine produces CRLF on disk.
    /// </summary>
    public partial class LinuxCompatDiagnosticsSession
    {
        private void ProbeTextStorage()
        {
            Section("Text storage write/exists/read/delete (Local/World/Global)");
            var utils = MyAPIGateway.Utilities;
            var owner = typeof(LinuxCompatDiagnosticsSession);

            // ---- Local ----
            CheckNoThrow(
                OwnerLinux,
                "Local write sentinel",
                () =>
                {
                    using (var w = utils.WriteFileInLocalStorage("sentinel.txt", owner))
                        w.WriteLine("local sentinel");
                }
            );
            CheckProbe(
                OwnerLinux,
                "Local exists after write",
                true,
                () => utils.FileExistsInLocalStorage("sentinel.txt", owner)
            );
            CheckProbe(
                OwnerLinux,
                "Local read first line",
                "local sentinel",
                () =>
                {
                    using (var r = utils.ReadFileInLocalStorage("sentinel.txt", owner))
                        return r == null ? null : r.ReadLine();
                }
            );
            CheckNoThrow(
                OwnerLinux,
                "Local delete sentinel",
                () => utils.DeleteFileInLocalStorage("sentinel.txt", owner)
            );
            CheckProbe(
                OwnerLinux,
                "Local exists after delete",
                false,
                () => utils.FileExistsInLocalStorage("sentinel.txt", owner)
            );

            // ---- World ----
            CheckNoThrow(
                OwnerLinux,
                "World write sentinel",
                () =>
                {
                    using (var w = utils.WriteFileInWorldStorage("sentinel.txt", owner))
                        w.WriteLine("world sentinel");
                }
            );
            CheckProbe(
                OwnerLinux,
                "World exists after write",
                true,
                () => utils.FileExistsInWorldStorage("sentinel.txt", owner)
            );
            CheckProbe(
                OwnerLinux,
                "World read first line",
                "world sentinel",
                () =>
                {
                    using (var r = utils.ReadFileInWorldStorage("sentinel.txt", owner))
                        return r == null ? null : r.ReadLine();
                }
            );
            CheckNoThrow(
                OwnerLinux,
                "World delete sentinel",
                () => utils.DeleteFileInWorldStorage("sentinel.txt", owner)
            );

            // ---- Global ----
            const string globalName = "LinuxCompatDiagnostics-global-sentinel.txt";
            CheckNoThrow(
                OwnerLinux,
                "Global write sentinel",
                () =>
                {
                    using (var w = utils.WriteFileInGlobalStorage(globalName))
                        w.WriteLine("global sentinel");
                }
            );
            CheckProbe(
                OwnerLinux,
                "Global exists after write",
                true,
                () => utils.FileExistsInGlobalStorage(globalName)
            );
            CheckProbe(
                OwnerLinux,
                "Global read first line",
                "global sentinel",
                () =>
                {
                    using (var r = utils.ReadFileInGlobalStorage(globalName))
                        return r == null ? null : r.ReadLine();
                }
            );
            CheckNoThrow(
                OwnerLinux,
                "Global delete sentinel",
                () => utils.DeleteFileInGlobalStorage(globalName)
            );
            CheckProbe(
                OwnerLinux,
                "Global exists after delete",
                false,
                () => utils.FileExistsInGlobalStorage(globalName)
            );

            // The writer's encoding: UTF-8 without BOM on both .NET Framework
            // and .NET 10 (StreamWriter(stream) default).
            CheckProbe(
                OwnerLinux,
                "Local writer Encoding.WebName",
                "utf-8",
                () =>
                {
                    using (var w = utils.WriteFileInLocalStorage("encoding-probe.txt", owner))
                        return w.Encoding == null ? null : w.Encoding.WebName;
                }
            );
            CheckNoThrow(
                OwnerLinux,
                "Local delete encoding probe",
                () => utils.DeleteFileInLocalStorage("encoding-probe.txt", owner)
            );
        }

        private void ProbeStorageCaseSensitivity()
        {
            Section("Storage case-insensitivity (Windows filesystems ignore case)");
            var utils = MyAPIGateway.Utilities;
            var owner = typeof(LinuxCompatDiagnosticsSession);

            CheckNoThrow(
                OwnerLinux,
                "write CaseSentinel.txt",
                () =>
                {
                    using (var w = utils.WriteFileInLocalStorage("CaseSentinel.txt", owner))
                        w.WriteLine("case sentinel");
                }
            );
            CheckProbe(
                OwnerLinux,
                "exists casesentinel.txt (lowercased)",
                true,
                () => utils.FileExistsInLocalStorage("casesentinel.txt", owner)
            );
            CheckProbe(
                OwnerLinux,
                "exists CASESENTINEL.TXT (uppercased)",
                true,
                () => utils.FileExistsInLocalStorage("CASESENTINEL.TXT", owner)
            );
            CheckProbe(
                OwnerLinux,
                "read casesentinel.TXT content",
                "case sentinel",
                () =>
                {
                    using (var r = utils.ReadFileInLocalStorage("casesentinel.TXT", owner))
                        return r == null ? null : r.ReadLine();
                }
            );
            // Delete through a differently-cased name must remove the file.
            CheckNoThrow(
                OwnerLinux,
                "delete CASESENTINEL.txt (uppercased)",
                () => utils.DeleteFileInLocalStorage("CASESENTINEL.txt", owner)
            );
            CheckProbe(
                OwnerLinux,
                "exists CaseSentinel.txt after delete",
                false,
                () => utils.FileExistsInLocalStorage("CaseSentinel.txt", owner)
            );
            // World and Global storage must be case-insensitive too (each has
            // its own directory resolution path in the wrapper).
            CheckNoThrow(
                OwnerLinux,
                "write WorldCase.txt (world storage)",
                () =>
                {
                    using (var w = utils.WriteFileInWorldStorage("WorldCase.txt", owner))
                        w.WriteLine("world case sentinel");
                }
            );
            CheckProbe(
                OwnerLinux,
                "exists worldcase.TXT (world, wrong case)",
                true,
                () => utils.FileExistsInWorldStorage("worldcase.TXT", owner)
            );
            CheckNoThrow(
                OwnerLinux,
                "delete WORLDCASE.txt (world, wrong case)",
                () => utils.DeleteFileInWorldStorage("WORLDCASE.txt", owner)
            );
            CheckProbe(
                OwnerLinux,
                "exists WorldCase.txt after wrong-case delete",
                false,
                () => utils.FileExistsInWorldStorage("WorldCase.txt", owner)
            );
            try
            {
                utils.DeleteFileInWorldStorage("WorldCase.txt", owner);
            }
            catch { }

            const string globalCase = "LinuxCompatDiagnostics-GlobalCase.txt";
            const string globalCaseWrong = "linuxcompatdiagnostics-globalcase.TXT";
            CheckNoThrow(
                OwnerLinux,
                "write GlobalCase sentinel (global storage)",
                () =>
                {
                    using (var w = utils.WriteFileInGlobalStorage(globalCase))
                        w.WriteLine("global case sentinel");
                }
            );
            CheckProbe(
                OwnerLinux,
                "exists global sentinel (wrong case)",
                true,
                () => utils.FileExistsInGlobalStorage(globalCaseWrong)
            );
            CheckNoThrow(
                OwnerLinux,
                "delete global sentinel (wrong case)",
                () => utils.DeleteFileInGlobalStorage(globalCaseWrong)
            );
            CheckProbe(
                OwnerLinux,
                "exists global sentinel after wrong-case delete",
                false,
                () => utils.FileExistsInGlobalStorage(globalCase)
            );
            try
            {
                utils.DeleteFileInGlobalStorage(globalCase);
            }
            catch { }

            // Cleanup in case the case-insensitive delete failed.
            try
            {
                MyAPIGateway.Utilities.DeleteFileInLocalStorage("CaseSentinel.txt", owner);
                MyAPIGateway.Utilities.DeleteFileInLocalStorage("casesentinel.txt", owner);
            }
            catch { }
        }

        private void ProbeStorageSeparators()
        {
            Section("Path separators in storage filenames (Windows rejects both)");
            var utils = MyAPIGateway.Utilities;
            var owner = typeof(LinuxCompatDiagnosticsSession);

            // '/' and '\' are both in MyKeenUtils.GetFixedInvalidFileNameChars:
            // storage names must be bare filenames on Windows.
            CheckThrows(
                OwnerLinux,
                "write sub/dir/f.txt",
                typeof(FileNotFoundException),
                () =>
                {
                    using (var w = utils.WriteFileInLocalStorage("sub/dir/f.txt", owner))
                        w.Write("x");
                }
            );
            CheckThrows(
                OwnerLinux,
                "write sub\\dir\\f.txt",
                typeof(FileNotFoundException),
                () =>
                {
                    using (var w = utils.WriteFileInLocalStorage("sub\\dir\\f.txt", owner))
                        w.Write("x");
                }
            );
            CheckProbe(
                OwnerLinux,
                "exists sub/dir/f.txt",
                false,
                () => utils.FileExistsInLocalStorage("sub/dir/f.txt", owner)
            );
            CheckProbe(
                OwnerLinux,
                "exists sub\\dir\\f.txt",
                false,
                () => utils.FileExistsInLocalStorage("sub\\dir\\f.txt", owner)
            );
            CheckThrows(
                OwnerLinux,
                "read sub/dir/f.txt",
                typeof(FileNotFoundException),
                () =>
                {
                    using (var r = utils.ReadFileInLocalStorage("sub/dir/f.txt", owner))
                        r.ReadLine();
                }
            );
            CheckNoThrow(
                OwnerLinux,
                "delete sub/dir/f.txt is a no-op",
                () => utils.DeleteFileInLocalStorage("sub/dir/f.txt", owner)
            );
            CheckNoThrow(
                OwnerLinux,
                "delete sub\\dir\\f.txt is a no-op",
                () => utils.DeleteFileInLocalStorage("sub\\dir\\f.txt", owner)
            );
        }

        private void ProbeStorageInvalidNames()
        {
            Section("Windows-invalid storage filenames (per character, per operation)");
            var utils = MyAPIGateway.Utilities;
            var owner = typeof(LinuxCompatDiagnosticsSession);

            string[] dirty =
            {
                "scrub:colon.txt",
                "scrub*asterisk.txt",
                "scrub?question.txt",
                "scrub|pipe.txt",
                "scrub<lt.txt",
                "scrub>gt.txt",
                "scrub\"quote.txt",
            };
            foreach (var name in dirty)
            {
                string captured = name; // C# 6: avoid loop-var capture
                string label = Fmt(captured);
                // Windows (Keen MyAPIUtilities): Write and Read throw
                // FileNotFoundException; FileExists returns false without
                // throwing; Delete checks FileExists first and no-ops.
                CheckThrows(
                    OwnerLinux,
                    "write " + label,
                    typeof(FileNotFoundException),
                    () =>
                    {
                        using (var w = utils.WriteFileInLocalStorage(captured, owner))
                            w.Write("x");
                    }
                );
                CheckProbe(
                    OwnerLinux,
                    "exists " + label + " returns false",
                    false,
                    () => utils.FileExistsInLocalStorage(captured, owner)
                );
                CheckThrows(
                    OwnerLinux,
                    "read " + label,
                    typeof(FileNotFoundException),
                    () =>
                    {
                        using (var r = utils.ReadFileInLocalStorage(captured, owner))
                            r.ReadLine();
                    }
                );
                CheckNoThrow(
                    OwnerLinux,
                    "delete " + label + " is a no-op",
                    () => utils.DeleteFileInLocalStorage(captured, owner)
                );
            }
        }

        private void ProbeCrlfOnDisk()
        {
            Section("On-disk line endings (text write, binary read-back)");
            var utils = MyAPIGateway.Utilities;
            var owner = typeof(LinuxCompatDiagnosticsSession);

            CheckNoThrow(
                OwnerLinux,
                "crlf-probe write two lines",
                () =>
                {
                    using (var w = utils.WriteFileInLocalStorage("crlf-probe.txt", owner))
                    {
                        w.WriteLine("A");
                        w.WriteLine("B");
                    }
                }
            );
            // Hex compare: WriteLine must have put CRLF on disk, no BOM.
            CheckProbe(
                OwnerLinux,
                "crlf-probe on-disk bytes",
                "41 0D 0A 42 0D 0A",
                () =>
                {
                    using (var r = utils.ReadBinaryFileInLocalStorage("crlf-probe.txt", owner))
                    {
                        if (r == null)
                            return "<null reader>";
                        byte[] buf = r.ReadBytes(64);
                        return HexBytes(buf);
                    }
                }
            );
            CheckNoThrow(
                OwnerLinux,
                "crlf-probe delete",
                () => utils.DeleteFileInLocalStorage("crlf-probe.txt", owner)
            );
        }

        private void ProbeReadToEnd()
        {
            Section("Multi-line ReadToEnd (line endings visible, unlike ReadLine)");
            var utils = MyAPIGateway.Utilities;
            var owner = typeof(LinuxCompatDiagnosticsSession);

            CheckNoThrow(
                OwnerLinux,
                "readtoend-probe write",
                () =>
                {
                    using (var w = utils.WriteFileInLocalStorage("readtoend-probe.txt", owner))
                    {
                        w.WriteLine("first");
                        w.WriteLine("second");
                        w.Write("third");
                    }
                }
            );
            CheckProbe(
                OwnerLinux,
                "ReadToEnd preserves CRLF",
                "first\r\nsecond\r\nthird",
                () =>
                {
                    using (var r = utils.ReadFileInLocalStorage("readtoend-probe.txt", owner))
                        return r == null ? null : r.ReadToEnd();
                }
            );
            CheckNoThrow(
                OwnerLinux,
                "readtoend-probe delete",
                () => utils.DeleteFileInLocalStorage("readtoend-probe.txt", owner)
            );
        }

        private void ProbeBinaryStorage()
        {
            Section("Binary storage round-trips (Local/World/Global)");
            var utils = MyAPIGateway.Utilities;
            var owner = typeof(LinuxCompatDiagnosticsSession);
            byte[] payload = { 0xDE, 0xAD, 0xBE, 0xEF, 0x42 };
            const string expectedHex = "DE AD BE EF 42";

            CheckNoThrow(
                OwnerLinux,
                "Local binary write",
                () =>
                {
                    using (var w = utils.WriteBinaryFileInLocalStorage("sentinel.bin", owner))
                        w.Write(payload);
                }
            );
            CheckProbe(
                OwnerLinux,
                "Local binary read-back",
                expectedHex,
                () =>
                {
                    using (var r = utils.ReadBinaryFileInLocalStorage("sentinel.bin", owner))
                        return r == null ? null : HexBytes(r.ReadBytes(payload.Length));
                }
            );
            CheckNoThrow(
                OwnerLinux,
                "Local binary delete",
                () => utils.DeleteFileInLocalStorage("sentinel.bin", owner)
            );

            CheckNoThrow(
                OwnerLinux,
                "World binary write",
                () =>
                {
                    using (var w = utils.WriteBinaryFileInWorldStorage("sentinel.bin", owner))
                        w.Write(payload);
                }
            );
            CheckProbe(
                OwnerLinux,
                "World binary read-back",
                expectedHex,
                () =>
                {
                    using (var r = utils.ReadBinaryFileInWorldStorage("sentinel.bin", owner))
                        return r == null ? null : HexBytes(r.ReadBytes(payload.Length));
                }
            );
            CheckNoThrow(
                OwnerLinux,
                "World binary delete",
                () => utils.DeleteFileInWorldStorage("sentinel.bin", owner)
            );

            const string globalBin = "LinuxCompatDiagnostics-global-sentinel.bin";
            CheckNoThrow(
                OwnerLinux,
                "Global binary write",
                () =>
                {
                    using (var w = utils.WriteBinaryFileInGlobalStorage(globalBin))
                        w.Write(payload);
                }
            );
            CheckProbe(
                OwnerLinux,
                "Global binary read-back",
                expectedHex,
                () =>
                {
                    using (var r = utils.ReadBinaryFileInGlobalStorage(globalBin))
                        return r == null ? null : HexBytes(r.ReadBytes(payload.Length));
                }
            );
            CheckNoThrow(
                OwnerLinux,
                "Global binary delete",
                () => utils.DeleteFileInGlobalStorage(globalBin)
            );
        }

        /// <summary>
        /// Windows share modes: while the writer handle handed out by a
        /// Write*Storage call is open, every other open of the same file fails
        /// with IOException (FileExists needs no handle and still answers
        /// true). Without that, a read landing inside the writer's window
        /// succeeds and returns zero bytes, which SerializeFromXML silently
        /// turns into null - the defect that crashed the Real* mod family
        /// during world load. IOException is not on the mod script whitelist,
        /// so these probes compare the thrown type's name instead of catching
        /// it.
        /// </summary>
        private void ProbeStorageSharing()
        {
            Section("Windows file sharing while a storage writer is open");
            var utils = MyAPIGateway.Utilities;
            var owner = typeof(LinuxCompatDiagnosticsSession);
            const string name = "sharing-probe.txt";
            const string payload = "sharing payload";
            const string globalName = "LinuxCompatDiagnostics-sharing-probe.txt";

            // ---- Local: the full set of overlapping operations ----
            TextWriter writer = null;
            try
            {
                writer = utils.WriteFileInLocalStorage(name, owner);
                writer.Write(payload);

                CheckProbe(
                    OwnerLinux,
                    "Local read while writer open",
                    "IOException",
                    () =>
                        ThrownTypeName(() =>
                        {
                            using (var r = utils.ReadFileInLocalStorage(name, owner))
                                return r == null ? null : r.ReadToEnd();
                        })
                );
                CheckProbe(
                    OwnerLinux,
                    "Local binary read while writer open",
                    "IOException",
                    () =>
                        ThrownTypeName(() =>
                        {
                            using (var r = utils.ReadBinaryFileInLocalStorage(name, owner))
                                return r == null ? null : HexBytes(r.ReadBytes(4));
                        })
                );
                CheckProbe(
                    OwnerLinux,
                    "Local second writer while writer open",
                    "IOException",
                    () =>
                        ThrownTypeName(() =>
                        {
                            // Reached only if the platform allowed the second
                            // handle; close it so the probe leaks nothing.
                            var second = utils.WriteFileInLocalStorage(name, owner);
                            second.Dispose();
                            return "<second writer opened>";
                        })
                );
                CheckProbe(
                    OwnerLinux,
                    "Local delete while writer open",
                    "IOException",
                    () =>
                        ThrownTypeName(() =>
                        {
                            utils.DeleteFileInLocalStorage(name, owner);
                            return null;
                        })
                );
                CheckProbe(
                    OwnerLinux,
                    "Local exists while writer open",
                    true,
                    () => utils.FileExistsInLocalStorage(name, owner)
                );
            }
            finally
            {
                if (writer != null)
                    writer.Dispose();
            }

            // The path is free again once the writer is disposed, and the
            // tracking decorator passed the payload through to disk.
            CheckProbe(
                OwnerLinux,
                "Local read after writer closed",
                payload,
                () =>
                {
                    using (var r = utils.ReadFileInLocalStorage(name, owner))
                        return r == null ? null : r.ReadToEnd();
                }
            );
            CheckNoThrow(
                OwnerLinux,
                "Local delete after writer closed",
                () => utils.DeleteFileInLocalStorage(name, owner)
            );

            // ---- World: the scope the crashing mods used ----
            TextWriter worldWriter = null;
            try
            {
                worldWriter = utils.WriteFileInWorldStorage(name, owner);
                worldWriter.Write(payload);
                CheckProbe(
                    OwnerLinux,
                    "World read while writer open",
                    "IOException",
                    () =>
                        ThrownTypeName(() =>
                        {
                            using (var r = utils.ReadFileInWorldStorage(name, owner))
                                return r == null ? null : r.ReadToEnd();
                        })
                );
            }
            finally
            {
                if (worldWriter != null)
                    worldWriter.Dispose();
            }
            CheckProbe(
                OwnerLinux,
                "World read after writer closed",
                payload,
                () =>
                {
                    using (var r = utils.ReadFileInWorldStorage(name, owner))
                        return r == null ? null : r.ReadToEnd();
                }
            );
            CheckNoThrow(
                OwnerLinux,
                "World delete after writer closed",
                () => utils.DeleteFileInWorldStorage(name, owner)
            );

            // ---- Global ----
            TextWriter globalWriter = null;
            try
            {
                globalWriter = utils.WriteFileInGlobalStorage(globalName);
                globalWriter.Write(payload);
                CheckProbe(
                    OwnerLinux,
                    "Global read while writer open",
                    "IOException",
                    () =>
                        ThrownTypeName(() =>
                        {
                            using (var r = utils.ReadFileInGlobalStorage(globalName))
                                return r == null ? null : r.ReadToEnd();
                        })
                );
            }
            finally
            {
                if (globalWriter != null)
                    globalWriter.Dispose();
            }
            CheckProbe(
                OwnerLinux,
                "Global read after writer closed",
                payload,
                () =>
                {
                    using (var r = utils.ReadFileInGlobalStorage(globalName))
                        return r == null ? null : r.ReadToEnd();
                }
            );
            CheckNoThrow(
                OwnerLinux,
                "Global delete after writer closed",
                () => utils.DeleteFileInGlobalStorage(globalName)
            );

            // ---- Binary writers hold the same lock and still round-trip ----
            const string binName = "sharing-probe.bin";
            byte[] binPayload = { 0x01, 0x02, 0x03, 0x04 };
            BinaryWriter binWriter = null;
            try
            {
                binWriter = utils.WriteBinaryFileInLocalStorage(binName, owner);
                binWriter.Write(binPayload);
                CheckProbe(
                    OwnerLinux,
                    "Local binary read while binary writer open",
                    "IOException",
                    () =>
                        ThrownTypeName(() =>
                        {
                            using (var r = utils.ReadBinaryFileInLocalStorage(binName, owner))
                                return r == null ? null : HexBytes(r.ReadBytes(binPayload.Length));
                        })
                );
            }
            finally
            {
                if (binWriter != null)
                    binWriter.Dispose();
            }
            CheckProbe(
                OwnerLinux,
                "Local binary read after writer closed",
                "01 02 03 04",
                () =>
                {
                    using (var r = utils.ReadBinaryFileInLocalStorage(binName, owner))
                        return r == null ? null : HexBytes(r.ReadBytes(binPayload.Length));
                }
            );
            CheckNoThrow(
                OwnerLinux,
                "Local binary delete after writer closed",
                () => utils.DeleteFileInLocalStorage(binName, owner)
            );
        }

        /// <summary>
        /// Name of the exception type the probe threw, or a marker holding the
        /// value it returned instead. Keeps exception types the mod script
        /// whitelist does not expose (IOException) out of this assembly.
        /// </summary>
        private static object ThrownTypeName(Func<object> probe)
        {
            try
            {
                return "<no exception: " + Fmt(probe()) + ">";
            }
            catch (Exception ex)
            {
                return ex.GetType().Name;
            }
        }

        private MyObjectBuilder_Checkpoint.ModItem OwnModItem()
        {
            return ModContext != null
                ? ModContext.ModItem
                : default(MyObjectBuilder_Checkpoint.ModItem);
        }

        private string ReadFirstLineInModLocation(string relative)
        {
            var utils = MyAPIGateway.Utilities;
            using (var r = utils.ReadFileInModLocation(relative, OwnModItem()))
                return r == null ? null : r.ReadLine();
        }

        private void ProbeModLocation()
        {
            Section("ReadFileInModLocation (case, separators, traversal, protection)");
            var utils = MyAPIGateway.Utilities;
            var me = OwnModItem();

            CheckProbe(
                OwnerLinux,
                "exists exact-case path",
                true,
                () => utils.FileExistsInModLocation("TestData/CaseSensitivity/expected.txt", me)
            );
            CheckProbe(
                OwnerLinux,
                "read exact-case path",
                "lowercase-content",
                () => ReadFirstLineInModLocation("TestData/CaseSensitivity/expected.txt")
            );

            CheckProbe(
                OwnerLinux,
                "exists UPPERCASED path",
                true,
                () => utils.FileExistsInModLocation("TESTDATA/CASESENSITIVITY/EXPECTED.TXT", me)
            );
            CheckProbe(
                OwnerLinux,
                "read UPPERCASED path",
                "lowercase-content",
                () => ReadFirstLineInModLocation("TESTDATA/CASESENSITIVITY/EXPECTED.TXT")
            );

            CheckProbe(
                OwnerLinux,
                "read lowercased path to Mixed.txt",
                "mixed-case-content",
                () => ReadFirstLineInModLocation("testdata/casesensitivity/mixed.txt")
            );

            CheckProbe(
                OwnerLinux,
                "exists backslash path",
                true,
                () => utils.FileExistsInModLocation("TestData\\CaseSensitivity\\expected.txt", me)
            );
            CheckProbe(
                OwnerLinux,
                "read backslash path",
                "lowercase-content",
                () => ReadFirstLineInModLocation("TestData\\CaseSensitivity\\expected.txt")
            );

            CheckProbe(
                OwnerLinux,
                "read nested subdir",
                "nested-content",
                () => ReadFirstLineInModLocation("TestData/Subdir/nested.txt")
            );

            // Traversal outside the mod folder must be rejected.
            CheckProbe(
                OwnerLinux,
                "exists ../../etc/passwd",
                false,
                () => utils.FileExistsInModLocation("../../etc/passwd", me)
            );
            CheckThrows(
                OwnerLinux,
                "read ../../etc/passwd",
                typeof(FileNotFoundException),
                () =>
                {
                    using (var r = utils.ReadFileInModLocation("../../etc/passwd", me))
                        r.ReadLine();
                }
            );

            // Data/Scripts is a protected location.
            CheckProbe(
                OwnerLinux,
                "exists protected Data/Scripts file",
                false,
                () =>
                    utils.FileExistsInModLocation(
                        "Data/Scripts/LinuxCompatDiagnostics/LinuxCompatDiagnostics.cs",
                        me
                    )
            );
            CheckThrows(
                OwnerLinux,
                "read protected Data/Scripts file",
                typeof(FileNotFoundException),
                () =>
                {
                    using (
                        var r = utils.ReadFileInModLocation(
                            "Data/Scripts/LinuxCompatDiagnostics/LinuxCompatDiagnostics.cs",
                            me
                        )
                    )
                        r.ReadLine();
                }
            );

            // NUL is in GetFixedInvalidPathChars: exists is false, read throws.
            CheckProbe(
                OwnerLinux,
                "exists NUL-char path",
                false,
                () => utils.FileExistsInModLocation("Test\0Data/x.txt", me)
            );
        }

        private void ProbeModLocationCasePair()
        {
            Section("Case-pair negative control (two files differing only in case)");
            // TestData/CaseSensitivity holds CasePair.txt and casepair.txt with
            // different content. Such a pair cannot exist on a Windows
            // filesystem, so there is no Windows reference for the ambiguous
            // lookup; the deterministic requirement is that an exact-case name
            // resolves to the exact-case file instead of its sibling.
            CheckProbe(
                OwnerLinux,
                "read CasePair.txt (exact) picks exact file",
                "UPPER-case-file",
                () => ReadFirstLineInModLocation("TestData/CaseSensitivity/CasePair.txt")
            );
            CheckProbe(
                OwnerLinux,
                "read casepair.txt (exact) picks exact file",
                "lower-case-file",
                () => ReadFirstLineInModLocation("TestData/CaseSensitivity/casepair.txt")
            );
            TryInfo(
                "read CASEPAIR.TXT (ambiguous, no Windows reference)",
                () => ReadFirstLineInModLocation("TestData/CaseSensitivity/CASEPAIR.TXT")
            );
        }

        private void ProbeModLocationAbsoluteRoundTrip()
        {
            Section("Ingress round-trip: ModPath egress fed back as absolute path");
            // Windows semantics: Path.Combine(modPath, absolute) returns the
            // absolute path, which still starts with modPath, so
            // ReadFileInModLocation accepts a full Windows-shaped path built
            // from the ModContext.ModPath egress value. This exercises the
            // egress -> mod arithmetic -> ingress funnel end to end.
            var ctx = ModContext;
            if (ctx == null || ctx.ModPath == null)
            {
                CheckTrue(OwnerLinux, "absolute round-trip preconditions", false, "<null ModPath>");
                return;
            }
            string absolute = ctx.ModPath + "\\TestData\\Subdir\\nested.txt";
            Info("absolute round-trip input", absolute);
            CheckProbe(
                OwnerLinux,
                "read via absolute Windows path",
                "nested-content",
                () => ReadFirstLineInModLocation(absolute)
            );

            string absoluteCombine = Path.Combine(ctx.ModPath, "TestData", "Subdir", "nested.txt");
            CheckProbe(
                OwnerLinux,
                "read via Path.Combine(ModPath, ...)",
                "nested-content",
                () => ReadFirstLineInModLocation(absoluteCombine)
            );

            string absoluteWrongCase = ctx.ModPath + "\\TESTDATA\\SUBDIR\\NESTED.TXT";
            CheckProbe(
                OwnerLinux,
                "read via absolute wrong-case path",
                "nested-content",
                () => ReadFirstLineInModLocation(absoluteWrongCase)
            );

            // Binary variant through the same funnel, with triangulation
            // probes that separate wrapper-side resolution from the inner
            // engine implementation.
            string absBin = ctx.ModPath + "\\TestData\\Binary\\payload.bin";
            TryInfo(
                "ModItem.GetPath() equals ModContext.ModPath",
                () => OwnModItem().GetPath() == ctx.ModPath
            );
            TryInfo(
                "exists via absolute binary path (wrapper resolution)",
                () => MyAPIGateway.Utilities.FileExistsInModLocation(absBin, OwnModItem())
            );
            CheckProbe(
                OwnerLinux,
                "binary read via relative path",
                "01 02 03 04 05 06 07 08",
                () =>
                {
                    var utils = MyAPIGateway.Utilities;
                    using (
                        var r = utils.ReadBinaryFileInModLocation(
                            "TestData\\Binary\\payload.bin",
                            OwnModItem()
                        )
                    )
                        return r == null ? null : HexBytes(r.ReadBytes(8));
                }
            );
            CheckProbe(
                OwnerLinux,
                "binary read via absolute path",
                "01 02 03 04 05 06 07 08",
                () =>
                {
                    var utils = MyAPIGateway.Utilities;
                    using (var r = utils.ReadBinaryFileInModLocation(absBin, OwnModItem()))
                        return r == null ? null : HexBytes(r.ReadBytes(8));
                }
            );
        }

        private void ProbeGameContent()
        {
            Section(
                "ReadFileInGameContent (case + separators, Data/CubeBlocks/CubeBlocks_Armor.sbc)"
            );
            var utils = MyAPIGateway.Utilities;
            const string armor = "Data/CubeBlocks/CubeBlocks_Armor.sbc";
            const string armorBs = "Data\\CubeBlocks\\CubeBlocks_Armor.sbc";
            const string armorLower = "data/cubeblocks/cubeblocks_armor.sbc";

            CheckProbe(
                OwnerLinux,
                "exists forward-slash",
                true,
                () => utils.FileExistsInGameContent(armor)
            );
            CheckProbe(
                OwnerLinux,
                "exists backslash",
                true,
                () => utils.FileExistsInGameContent(armorBs)
            );
            CheckProbe(
                OwnerLinux,
                "exists lowercased",
                true,
                () => utils.FileExistsInGameContent(armorLower)
            );

            // First line makes BOM/encoding handling visible (defect 4).
            string firstLine = null;
            try
            {
                using (var r = utils.ReadFileInGameContent(armor))
                    firstLine = r == null ? null : r.ReadLine();
            }
            catch (Exception ex)
            {
                firstLine = "<EXCEPTION: " + ex.GetType().Name + ": " + ex.Message + ">";
            }
            Info("game content first line", firstLine);
            CheckTrue(
                OwnerLinux,
                "game content first line is XML declaration",
                firstLine != null && firstLine.StartsWith("<?xml"),
                firstLine
            );

            // Raw first bytes expose any BOM.
            CheckProbe(
                OwnerLinux,
                "game content first bytes (BOM + '<?xml')",
                "EF BB BF 3C 3F 78 6D 6C",
                () =>
                {
                    using (var r = utils.ReadBinaryFileInGameContent(armor))
                        return r == null ? null : HexBytes(r.ReadBytes(8));
                }
            );
            CheckProbe(
                OwnerLinux,
                "binary read via backslash path",
                "EF BB BF 3C 3F 78 6D 6C",
                () =>
                {
                    using (var r = utils.ReadBinaryFileInGameContent(armorBs))
                        return r == null ? null : HexBytes(r.ReadBytes(8));
                }
            );

            CheckProbe(
                OwnerLinux,
                "exists NUL-char content path",
                false,
                () => utils.FileExistsInGameContent("Data\0CubeBlocks.sbc")
            );
        }

        private void ProbeGameContentAbsoluteRoundTrip()
        {
            Section("Ingress round-trip: ContentPath egress fed back as absolute path");
            var utils = MyAPIGateway.Utilities;
            var gp = MyAPIGateway.Utilities.GamePaths;
            if (gp == null || gp.ContentPath == null)
            {
                CheckTrue(OwnerLinux, "content absolute round-trip preconditions", false, "<null>");
                return;
            }
            string absolute = Path.Combine(
                gp.ContentPath,
                "Data",
                "CubeBlocks",
                "CubeBlocks_Armor.sbc"
            );
            Info("content absolute round-trip input", absolute);
            CheckProbe(
                OwnerLinux,
                "exists via absolute content path",
                true,
                () => utils.FileExistsInGameContent(absolute)
            );
            CheckProbe(
                OwnerLinux,
                "read via absolute content path first bytes",
                "EF BB BF 3C 3F 78 6D 6C",
                () =>
                {
                    using (var r = utils.ReadBinaryFileInGameContent(absolute))
                        return r == null ? null : HexBytes(r.ReadBytes(8));
                }
            );
        }
    }
}
