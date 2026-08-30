using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Utils;

namespace LinuxCompatDiagnostics
{
    /// <summary>
    /// Automated test suite for the mod API boundary of the se-linux-compat
    /// plugin, run inside the game as a session component.
    ///
    /// Every probe is an expected-vs-actual assertion tagged with the plugin
    /// that owns the behavior:
    ///   [LINUX]  - Linux-only semantics (paths, separators, drive letters,
    ///              casing, CRLF on disk, Stopwatch shims). Expected value =
    ///              what the same mod observes on Windows. FAILs are
    ///              se-linux-compat bugs.
    ///   [DOTNET] - .NET 10 vs .NET Framework semantics that reproduce on
    ///              Windows too (Encoding.Default, codepages, ICU collation,
    ///              culture formatting). Expected value = raw .NET 10 on
    ///              Windows. FAILs are follow-up items for se-dotnet-compat,
    ///              NOT linux-compat bugs (they may also indicate an active
    ///              dotnet-compat shim; verify intent there).
    ///
    /// Output goes to LocalStorage/LinuxCompatDiagnostics.log:
    ///   PASS [LINUX] name | expected=... | actual=...
    ///   FAIL [DOTNET] name | expected=... | actual=...
    ///   INFO name = value
    ///   === SUMMARY pass=N fail=N linux_fail=N dotnet_fail=N info=N ===
    ///   === END LinuxCompatDiagnostics ===
    /// The END line is the terminator; a log without it means the run died
    /// mid-suite. Every FAIL and the summary are mirrored to MyLog.Default
    /// (SpaceEngineers.log) so results survive even if the storage API under
    /// test is broken.
    ///
    /// Constraints observed by the in-game C# compiler / whitelist:
    ///   - C# 6 semantics only (no "is X y", no "using var", no tuples,
    ///     no out-var declarations).
    ///   - StreamReader/StreamWriter/MemoryStream are not whitelisted; use
    ///     TextReader/TextWriter/BinaryReader/BinaryWriter.
    ///   - Environment: only CurrentManagedThreadId, NewLine and
    ///     ProcessorCount are whitelisted (MySpaceGameDefaultIlChecker);
    ///     referencing any other member fails IL check and kills the mod, so
    ///     the prohibited members are documented as INFO lines instead.
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public partial class LinuxCompatDiagnosticsSession : MySessionComponentBase
    {
        private const string LogFile = "LinuxCompatDiagnostics.log";
        private const string Tag = "[LinuxCompatDiagnostics]";

        private const string OwnerLinux = "[LINUX]";
        private const string OwnerDotnet = "[DOTNET]";

        // Unique mod-message channel id for the messaging round-trip probe.
        private const long ModMessageChannelId = 0x4C43445858444941L; // "LCDXXDIA"

        private readonly StringBuilder _sb = new StringBuilder(64 * 1024);

        private int _passCount;
        private int _failLinuxCount;
        private int _failDotnetCount;
        private int _infoCount;

        // Async probe state, set by callbacks and collected on update ticks.
        private int _invokeOnGameThreadCount;
        private int _modMessageReceivedCount;
        private object _modMessageLastPayload;
        private Action<object> _modMessageHandler;

        private int _updateTicks;
        private bool _finalized;

        // Give async callbacks (InvokeOnGameThread, SendModMessage dispatch)
        // this many ticks to arrive before finalizing anyway.
        private const int MaxWaitTicks = 120;

        public override void LoadData()
        {
            try
            {
                Line("=== LinuxCompatDiagnostics v2 ===");
                Info("suite started (UTC)", DateTime.UtcNow.ToString("o"));

                ProbeRewriterIdentity();
                ProbeEnvironment();
                ProbePathStaticMembers();
                ProbePathMethods();
                ProbeGamePaths();
                ProbeConfigDedicated();
                ProbeSessionPaths();
                ProbeOwnModContext();
                ProbeLoadedMods();
                ProbeTextStorage();
                ProbeStorageCaseSensitivity();
                ProbeStorageSeparators();
                ProbeStorageInvalidNames();
                ProbeCrlfOnDisk();
                ProbeReadToEnd();
                ProbeBinaryStorage();
                ProbeModLocation();
                ProbeModLocationCasePair();
                ProbeModLocationAbsoluteRoundTrip();
                ProbeGameContent();
                ProbeGameContentAbsoluteRoundTrip();
                ProbeSerializeXml();
                ProbeSerializeBinary();
                ProbeSessionVariables();
                ProbeOtherUtilities();
                ProbeMessagingSetup();
                ProbeCulture();
                ProbeCollation();
                ProbeEncodings();
                ProbeStopwatch();
                ProbeStringBuilderCrlf();
                ProbeThreadingPrimitives();
                ProbeTypeIdentity();
                ProbeDefinitionPipeline();
                DumpUntestable();

                // Partial flush without terminator: if the game dies before
                // the update-phase probes run, the harness sees an
                // incomplete-run marker instead of silence.
                Flush();
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole(Tag + " FATAL in LoadData: " + ex);
                try
                {
                    Line("FATAL in LoadData: " + ex);
                    Flush();
                }
                catch { }
            }
        }

        public override void UpdateBeforeSimulation()
        {
            if (_finalized)
                return;
            _updateTicks++;

            try
            {
                if (_updateTicks == 1)
                {
                    // Entity spawning must happen on the game thread with the
                    // session fully loaded; the first update tick is the
                    // earliest safe point.
                    ProbeIngressSpawns();
                    return;
                }

                bool asyncDone = _invokeOnGameThreadCount >= 1 && _modMessageReceivedCount >= 1;
                if (!asyncDone && _updateTicks < MaxWaitTicks)
                    return;

                FinalizeSuite(asyncDone);
            }
            catch (Exception ex)
            {
                MyLog.Default.WriteLineAndConsole(Tag + " update phase failed: " + ex);
                try
                {
                    Line("FATAL in update phase: " + ex);
                    FinalizeSuite(false);
                }
                catch { }
            }
        }

        private void FinalizeSuite(bool asyncDone)
        {
            _finalized = true;

            Section("Async probes (collected after " + _updateTicks + " ticks)");
            CheckEquals(
                OwnerLinux,
                "InvokeOnGameThread callback ran once",
                1,
                _invokeOnGameThreadCount
            );
            CheckEquals(OwnerLinux, "SendModMessage handler ran once", 1, _modMessageReceivedCount);
            CheckEquals(
                OwnerLinux,
                "SendModMessage payload round-trip",
                "LinuxCompatDiagnostics probe payload",
                _modMessageLastPayload as string
            );
            if (!asyncDone)
                Info("async wait", "timed out after " + _updateTicks + " ticks");

            TryVoidInfo("UnregisterMessageHandler", UnregisterModMessageHandler);

            int fail = _failLinuxCount + _failDotnetCount;
            string summary =
                "=== SUMMARY pass="
                + _passCount
                + " fail="
                + fail
                + " linux_fail="
                + _failLinuxCount
                + " dotnet_fail="
                + _failDotnetCount
                + " info="
                + _infoCount
                + " ===";
            Line("");
            Line(summary);
            Line("=== END LinuxCompatDiagnostics ===");
            MyLog.Default.WriteLineAndConsole(Tag + " " + summary);

            Flush();

            // The suite is done; stop consuming update time (defect 6).
            SetUpdateOrder(MyUpdateOrder.NoUpdate);
        }

        private void UnregisterModMessageHandler()
        {
            if (_modMessageHandler == null)
                return;
            MyAPIGateway.Utilities.UnregisterMessageHandler(
                ModMessageChannelId,
                _modMessageHandler
            );
            _modMessageHandler = null;
        }

        protected override void UnloadData()
        {
            try
            {
                UnregisterModMessageHandler();
            }
            catch { }
        }

        // ----- assertion helpers -----

        private void Section(string name)
        {
            Line("");
            Line("--- " + name + " ---");
        }

        private void Line(string s)
        {
            _sb.Append(s).Append('\n');
        }

        private static string Fmt(object value)
        {
            if (value == null)
                return "<null>";
            string s = value as string;
            if (s != null)
            {
                var b = new StringBuilder(s.Length + 8);
                b.Append('"');
                for (int i = 0; i < s.Length; i++)
                {
                    char c = s[i];
                    if (c == '\\')
                        b.Append("\\\\");
                    else if (c == '"')
                        b.Append("\\\"");
                    else if (c == '\r')
                        b.Append("\\r");
                    else if (c == '\n')
                        b.Append("\\n");
                    else if (c == '\t')
                        b.Append("\\t");
                    else if (c < ' ')
                        b.Append("\\x").Append(((int)c).ToString("X2"));
                    else
                        b.Append(c);
                }
                b.Append('"');
                return b.ToString();
            }
            if (value is bool)
                return (bool)value ? "True" : "False";
            return value.ToString();
        }

        private void Report(string owner, string name, bool pass, string expected, string actual)
        {
            string line =
                (pass ? "PASS " : "FAIL ")
                + owner
                + " "
                + name
                + " | expected="
                + expected
                + " | actual="
                + actual;
            Line(line);
            if (pass)
            {
                _passCount++;
            }
            else
            {
                if (owner == OwnerDotnet)
                    _failDotnetCount++;
                else
                    _failLinuxCount++;
                // Mirror failures to the game log so they survive a broken
                // storage API (defect 5).
                MyLog.Default.WriteLineAndConsole(Tag + " " + line);
            }
        }

        private void CheckEquals(string owner, string name, object expected, object actual)
        {
            bool pass = expected == null ? actual == null : expected.Equals(actual);
            Report(owner, name, pass, Fmt(expected), Fmt(actual));
        }

        private void CheckTrue(string owner, string name, bool condition, object actual)
        {
            Report(owner, name, condition, "<true>", Fmt(actual));
        }

        /// <summary>Runs the probe; PASS if it returns a value equal to expected, FAIL on mismatch or exception.</summary>
        private void CheckProbe(string owner, string name, object expected, Func<object> probe)
        {
            object actual;
            try
            {
                actual = probe();
            }
            catch (Exception ex)
            {
                Report(
                    owner,
                    name,
                    false,
                    Fmt(expected),
                    "<EXCEPTION: " + ex.GetType().Name + ": " + ex.Message + ">"
                );
                return;
            }
            CheckEquals(owner, name, expected, actual);
        }

        /// <summary>PASS if the probe throws the expected exception type; FAIL on no throw or a different type.</summary>
        private void CheckThrows(string owner, string name, Type exceptionType, Action probe)
        {
            string expected = "<throws " + exceptionType.Name + ">";
            try
            {
                probe();
            }
            catch (Exception ex)
            {
                // Type.IsAssignableFrom is not mod-whitelisted; the expected
                // exception types are sealed leaves anyway, so exact match.
                bool pass = ex.GetType() == exceptionType;
                Report(
                    owner,
                    name,
                    pass,
                    expected,
                    "<EXCEPTION: " + ex.GetType().Name + ": " + ex.Message + ">"
                );
                return;
            }
            Report(owner, name, false, expected, "<no exception>");
        }

        /// <summary>PASS if the probe completes without throwing.</summary>
        private void CheckNoThrow(string owner, string name, Action probe)
        {
            try
            {
                probe();
            }
            catch (Exception ex)
            {
                Report(
                    owner,
                    name,
                    false,
                    "<no exception>",
                    "<EXCEPTION: " + ex.GetType().Name + ": " + ex.Message + ">"
                );
                return;
            }
            Report(owner, name, true, "<no exception>", "<no exception>");
        }

        private void Info(string name, object value)
        {
            Line("INFO " + name + " = " + Fmt(value));
            _infoCount++;
        }

        private void TryInfo(string name, Func<object> probe)
        {
            try
            {
                Info(name, probe());
            }
            catch (Exception ex)
            {
                Info(name, "<EXCEPTION: " + ex.GetType().Name + ": " + ex.Message + ">");
            }
        }

        private void TryVoidInfo(string name, Action probe)
        {
            try
            {
                probe();
                Info(name, "<ok>");
            }
            catch (Exception ex)
            {
                Info(name, "<EXCEPTION: " + ex.GetType().Name + ": " + ex.Message + ">");
            }
        }

        // ----- shared helpers -----

        private static bool IsDriveRooted(string path)
        {
            return path != null
                && path.Length >= 3
                && path[1] == ':'
                && path[2] == '\\'
                && ((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z'));
        }

        /// <summary>Windows-shape check: drive-rooted and free of forward slashes.</summary>
        private void CheckWindowsShapedAbsolute(string name, string path)
        {
            CheckTrue(
                OwnerLinux,
                name + " is Windows-shaped absolute",
                IsDriveRooted(path) && path.IndexOf('/') < 0,
                path
            );
        }

        private static string HexBytes(byte[] data)
        {
            if (data == null)
                return "<null>";
            var sb = new StringBuilder(data.Length * 3);
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0)
                    sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }

        private static bool EndsWithIgnoreCase(string value, string suffix)
        {
            return value != null
                && value.Length >= suffix.Length
                && string.Compare(
                    value,
                    value.Length - suffix.Length,
                    suffix,
                    0,
                    suffix.Length,
                    StringComparison.OrdinalIgnoreCase
                ) == 0;
        }

        private void Flush()
        {
            try
            {
                using (
                    var w = MyAPIGateway.Utilities.WriteFileInLocalStorage(
                        LogFile,
                        typeof(LinuxCompatDiagnosticsSession)
                    )
                )
                {
                    w.Write(_sb.ToString());
                    w.Flush();
                }
                MyLog.Default.WriteLineAndConsole(
                    Tag + " wrote diagnostics to LocalStorage/" + LogFile
                );
            }
            catch (Exception ex)
            {
                // The storage API itself is under test: if it fails, dump the
                // whole buffer into the game log so nothing is lost.
                MyLog.Default.WriteLineAndConsole(Tag + " failed to write log file: " + ex);
                MyLog.Default.WriteLineAndConsole(Tag + " BEGIN inline dump");
                MyLog.Default.WriteLine(_sb.ToString());
                MyLog.Default.WriteLineAndConsole(Tag + " END inline dump");
            }
        }

        // ----- untestable surface -----

        private void DumpUntestable()
        {
            Section("Members of the wrapped Mod API NOT covered here");
            Line("Intentionally skipped: user-visible side effects, interactive input,");
            Line("or session state that cannot be staged in an automated run:");
            Line("  IMyUtilities.ShowNotification    - flashes a HUD notification");
            Line("  IMyUtilities.ShowMessage         - posts to the chat HUD");
            Line("  IMyUtilities.SendMessage         - sends as the local player in chat");
            Line("  IMyUtilities.ShowMissionScreen   - modal screen blocks input");
            Line("  IMyUtilities.GetObjectiveLine    - tied to campaign objective state");
            Line("  IMyUtilities.MessageEntered/MessageEnteredSender/MessageRecieved");
            Line("                                   - events, require chat traffic");
        }
    }
}
