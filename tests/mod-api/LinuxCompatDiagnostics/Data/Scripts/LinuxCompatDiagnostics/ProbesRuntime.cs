using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Sandbox.ModAPI;
using VRageSpinLock = VRage.Library.Threading.SpinLock;

namespace LinuxCompatDiagnostics
{
    /// <summary>
    /// Runtime-semantics probes.
    ///
    /// [LINUX] probes cover the rewriter shims (Stopwatch, Environment.NewLine,
    /// StringBuilder.AppendLine) whose expected values equal Windows behavior.
    ///
    /// [DOTNET] probes cover .NET 10 vs .NET Framework differences that
    /// reproduce on Windows too. Their expected values were captured from a
    /// raw .NET 10 runtime; a FAIL means either a dotnet-compat shim is
    /// active (verify intent in that repo) or a real dotnet-compat gap.
    /// Framework values are noted in comments where they differ.
    /// </summary>
    public partial class LinuxCompatDiagnosticsSession
    {
        private void ProbeRewriterIdentity()
        {
            Section("Rewriter detection (linux-compat compilation rewriter)");
            // The rewriter substitutes System.Diagnostics.Stopwatch with the
            // plugin's WindowsStopwatch shim in compiled mod code. Seeing the
            // shim type here proves this mod went through the linux-compat
            // rewriter (dotnet-compat's rewriter only touches mods that fail
            // to compile, never this one). The namespace root differs between
            // the client and server plugin builds.
            string stopwatchType = typeof(Stopwatch).FullName;
            CheckTrue(
                OwnerLinux,
                "typeof(Stopwatch).FullName is the shim",
                stopwatchType != null && stopwatchType.EndsWith(".Rewriter.WindowsStopwatch"),
                stopwatchType
            );
        }

        private void ProbeCulture()
        {
            Section("Culture defaults and formatting");
            // The game runs with the invariant culture on all platforms.
            CheckProbe(
                OwnerDotnet,
                "CurrentCulture.Name is invariant",
                "",
                () => CultureInfo.CurrentCulture.Name
            );
            TryInfo("CurrentUICulture.Name", () => CultureInfo.CurrentUICulture.Name);
            CheckProbe(
                OwnerDotnet,
                "invariant NumberDecimalSeparator",
                ".",
                () => CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator
            );
            CheckProbe(
                OwnerDotnet,
                "invariant NumberGroupSeparator",
                ",",
                () => CultureInfo.CurrentCulture.NumberFormat.NumberGroupSeparator
            );
            CheckProbe(
                OwnerDotnet,
                "invariant ShortDatePattern",
                "MM/dd/yyyy",
                () => CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern
            );

            CheckProbe(
                OwnerDotnet,
                "(1.5).ToString() default culture",
                "1.5",
                () => (1.5).ToString()
            );
            CheckProbe(
                OwnerDotnet,
                "(1234.5).ToString(N2) default culture",
                "1,234.50",
                () => (1234.5).ToString("N2")
            );
            CheckProbe(
                OwnerDotnet,
                "double.Parse(1.5) default culture",
                "1.5",
                () => double.Parse("1.5").ToString(CultureInfo.InvariantCulture)
            );
            // Invariant culture reads ',' as a group separator: "1,5" -> 15.
            CheckProbe(
                OwnerDotnet,
                "double.Parse(1,5) default culture",
                "15",
                () => double.Parse("1,5").ToString(CultureInfo.InvariantCulture)
            );
            CheckThrows(
                OwnerDotnet,
                "int.Parse(1,234) default culture",
                typeof(FormatException),
                () => int.Parse("1,234")
            );

            // Comma-decimal culture: explicit de-DE formatting and parsing.
            var de = CultureInfo.GetCultureInfo("de-DE");
            CheckProbe(
                OwnerDotnet,
                "de-DE (1234.5).ToString(N2)",
                "1.234,50",
                () => (1234.5).ToString("N2", de)
            );
            CheckProbe(
                OwnerDotnet,
                "de-DE double.Parse(1,5)",
                "1.5",
                () => double.Parse("1,5", de).ToString(CultureInfo.InvariantCulture)
            );
            CheckProbe(
                OwnerDotnet,
                "de-DE NumberDecimalSeparator",
                ",",
                () => de.NumberFormat.NumberDecimalSeparator
            );

            // ICU gives fr-FR a narrow no-break space (U+202F) group
            // separator; .NET Framework NLS used U+00A0.
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            CheckProbe(
                OwnerDotnet,
                "fr-FR group separator codepoint (ICU U+202F)",
                "202F",
                () =>
                {
                    string sep = fr.NumberFormat.NumberGroupSeparator;
                    return sep.Length == 1
                        ? ((int)sep[0]).ToString("X4")
                        : "<len " + sep.Length + ">";
                }
            );

            // Turkish casing must work through explicit culture.
            CheckProbe(
                OwnerDotnet,
                "tr-TR 'i'.ToUpper() is U+0130",
                "0130",
                () => ((int)"i".ToUpper(CultureInfo.GetCultureInfo("tr-TR"))[0]).ToString("X4")
            );
            CheckProbe(OwnerDotnet, "'I'.ToLowerInvariant()", "i", () => "I".ToLowerInvariant());

            // DateTime round-trips with the invariant default culture.
            var sample = new DateTime(2030, 1, 2, 13, 45, 0, DateTimeKind.Utc);
            CheckProbe(
                OwnerDotnet,
                "DateTime.ToString() default culture",
                "01/02/2030 13:45:00",
                () => sample.ToString()
            );
            CheckProbe(
                OwnerDotnet,
                "DateTime.ToString(o)",
                "2030-01-02T13:45:00.0000000Z",
                () => sample.ToString("o")
            );
            CheckProbe(
                OwnerDotnet,
                "DateTime.Parse(01/02/2030) MM/dd",
                "2030-01-02T00:00:00.0000000",
                () => DateTime.Parse("01/02/2030").ToString("o")
            );
            TryInfo("DateTime.Now.Kind", () => DateTime.Now.Kind);
            TryInfo("DateTime.UtcNow (o)", () => DateTime.UtcNow.ToString("o"));
            TryInfo(
                "local UTC offset minutes",
                () => Math.Round((DateTime.Now - DateTime.UtcNow).TotalMinutes)
            );
        }

        private void ProbeCollation()
        {
            Section("Collation and culture-sensitive search (ICU on .NET 10)");
            CheckProbe(
                OwnerDotnet,
                "Compare(a, a-umlaut) invariant",
                -1,
                () => Math.Sign(string.Compare("a", "ä", StringComparison.InvariantCulture))
            );
            CheckProbe(
                OwnerDotnet,
                "Compare(a, a-umlaut) ordinal",
                -131,
                () => string.Compare("a", "ä", StringComparison.Ordinal)
            );
            // Famous ICU vs NLS break: linguistic IndexOf no longer matches
            // "ss" inside "strasse-with-sharp-s" (.NET Framework returned 4).
            CheckProbe(
                OwnerDotnet,
                "strasse(sharp-s).IndexOf(ss) culture-sensitive",
                -1,
                () => "straße".IndexOf("ss", StringComparison.InvariantCulture)
            );
            CheckProbe(
                OwnerDotnet,
                "strasse(sharp-s).IndexOf(ss) ordinal",
                -1,
                () => "straße".IndexOf("ss", StringComparison.Ordinal)
            );
            CheckProbe(
                OwnerDotnet,
                "Compare(AE, AE-ligature) ignore-case",
                -1,
                () =>
                    Math.Sign(
                        string.Compare("AE", "Æ", StringComparison.InvariantCultureIgnoreCase)
                    )
            );
            CheckProbe(
                OwnerDotnet,
                "OrdinalIgnoreCase file/FILE equality",
                true,
                () => "file".Equals("FILE", StringComparison.OrdinalIgnoreCase)
            );

            // Ordering with an explicit comparer culture.
            CheckProbe(
                OwnerDotnet,
                "de-DE sort of [b, a, a-umlaut]",
                "a,ä,b",
                () =>
                {
                    var items = new List<string> { "b", "a", "ä" };
                    items.Sort(StringComparer.Create(CultureInfo.GetCultureInfo("de-DE"), false));
                    return string.Join(",", items);
                }
            );

            // Regex culture handling.
            CheckProbe(
                OwnerDotnet,
                "Regex ^i$ IgnoreCase matches I",
                true,
                () => new Regex("^i$", RegexOptions.IgnoreCase).IsMatch("I")
            );
            CheckProbe(
                OwnerDotnet,
                "Regex ^i$ IgnoreCase|CultureInvariant matches I",
                true,
                () =>
                    new Regex(
                        "^i$",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
                    ).IsMatch("I")
            );
        }

        private void ProbeEncodings()
        {
            Section("Encoding defaults and codepages");
            // Raw .NET 10: Encoding.Default is UTF-8 (.NET Framework on
            // Windows gave the ANSI codepage, e.g. Windows-1252).
            CheckProbe(
                OwnerDotnet,
                "Encoding.Default.WebName",
                "utf-8",
                () => Encoding.Default.WebName
            );
            CheckProbe(
                OwnerDotnet,
                "Encoding.Default.CodePage",
                65001,
                () => Encoding.Default.CodePage
            );
            // Raw .NET 10 has no codepage 1252 without
            // CodePagesEncodingProvider; Framework returned Windows-1252.
            // A PASS here means nothing registered the provider; if this
            // FAILs with windows-1252, dotnet-compat (or the game) registered
            // it - verify the intent in the dotnet-compat repo.
            CheckThrows(
                OwnerDotnet,
                "Encoding.GetEncoding(1252) unavailable",
                typeof(NotSupportedException),
                () => Encoding.GetEncoding(1252)
            );
            CheckProbe(OwnerDotnet, "Encoding.UTF8.WebName", "utf-8", () => Encoding.UTF8.WebName);
            CheckProbe(
                OwnerDotnet,
                "Encoding.UTF8.GetPreamble().Length",
                3,
                () => Encoding.UTF8.GetPreamble().Length
            );
            CheckProbe(
                OwnerDotnet,
                "Encoding.Unicode.WebName",
                "utf-16",
                () => Encoding.Unicode.WebName
            );
            CheckProbe(
                OwnerDotnet,
                "Encoding.ASCII.WebName",
                "us-ascii",
                () => Encoding.ASCII.WebName
            );
        }

        private void ProbeStopwatch()
        {
            Section("Stopwatch (rewritten to WindowsStopwatch shim)");
            // Windows QueryPerformanceCounter reports 10 MHz (100ns ticks).
            CheckProbe(OwnerLinux, "Stopwatch.Frequency", 10000000L, () => Stopwatch.Frequency);
            CheckProbe(
                OwnerLinux,
                "Stopwatch.IsHighResolution",
                true,
                () => Stopwatch.IsHighResolution
            );

            long t1 = Stopwatch.GetTimestamp();
            long t2 = Stopwatch.GetTimestamp();
            CheckTrue(OwnerLinux, "GetTimestamp is monotonic", t2 >= t1, t1 + " -> " + t2);
            Info("Stopwatch.GetTimestamp()", t1);
            // Baseline sanity: with a 10 MHz frequency, a boot- or
            // process-relative timestamp stays under 30 days.
            CheckTrue(
                OwnerLinux,
                "GetTimestamp under 30 days of ticks",
                t1 >= 0 && t1 < 10000000L * 86400L * 30L,
                t1
            );

            // Elapsed vs wall clock: busy-wait ~20ms measured by
            // DateTime.UtcNow and compare against the shim's elapsed values.
            var sw = Stopwatch.StartNew();
            DateTime start = DateTime.UtcNow;
            double wallMs = 0;
            while (wallMs < 20.0)
                wallMs = (DateTime.UtcNow - start).TotalMilliseconds;
            sw.Stop();

            double swMs = sw.Elapsed.TotalMilliseconds;
            Info("busy-wait wall-clock ms", wallMs.ToString("F3", CultureInfo.InvariantCulture));
            Info("busy-wait sw.Elapsed ms", swMs.ToString("F3", CultureInfo.InvariantCulture));
            CheckTrue(
                OwnerLinux,
                "Elapsed tracks wall clock (+-15ms)",
                Math.Abs(swMs - wallMs) < 15.0,
                "wall="
                    + wallMs.ToString("F1", CultureInfo.InvariantCulture)
                    + " sw="
                    + swMs.ToString("F1", CultureInfo.InvariantCulture)
            );
            // ElapsedTicks must be in 100ns units: ticks / 10000 == ms.
            double ticksAsMs = sw.ElapsedTicks / 10000.0;
            CheckTrue(
                OwnerLinux,
                "ElapsedTicks are 100ns units",
                Math.Abs(ticksAsMs - swMs) < 5.0,
                "ticks="
                    + sw.ElapsedTicks
                    + " (="
                    + ticksAsMs.ToString("F1", CultureInfo.InvariantCulture)
                    + "ms)"
            );
            CheckTrue(
                OwnerLinux,
                "ElapsedMilliseconds consistent",
                Math.Abs(sw.ElapsedMilliseconds - swMs) <= 1.5,
                sw.ElapsedMilliseconds
            );
        }

        private void ProbeStringBuilderCrlf()
        {
            Section("StringBuilder.AppendLine (rewritten to CRLF)");
            CheckProbe(
                OwnerLinux,
                "AppendLine(x) yields x + CRLF",
                "x\r\n",
                () =>
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("x");
                    return sb.ToString();
                }
            );
            CheckProbe(
                OwnerLinux,
                "AppendLine() yields CRLF",
                "\r\n",
                () =>
                {
                    var sb = new StringBuilder();
                    sb.AppendLine();
                    return sb.ToString();
                }
            );
        }

        private void ProbeThreadingPrimitives()
        {
            Section("Threading primitives (whitelisted subset)");
            CheckProbe(
                OwnerDotnet,
                "Interlocked.Increment x10000",
                10000,
                () =>
                {
                    int counter = 0;
                    for (int i = 0; i < 10000; i++)
                        Interlocked.Increment(ref counter);
                    return counter;
                }
            );
            CheckProbe(
                OwnerDotnet,
                "Monitor.TryEnter uncontended",
                true,
                () =>
                {
                    object lockObj = new object();
                    bool taken = false;
                    Monitor.TryEnter(lockObj, 0, ref taken);
                    if (taken)
                        Monitor.Exit(lockObj);
                    return taken;
                }
            );
            CheckNoThrow(
                OwnerDotnet,
                "VRage SpinLock acquire/release",
                () =>
                {
                    VRageSpinLock spin = new VRageSpinLock();
                    spin.Enter();
                    spin.Exit();
                }
            );
            CheckNoThrow(
                OwnerDotnet,
                "AutoResetEvent construct/dispose",
                () =>
                {
                    using (var are = new AutoResetEvent(true)) { }
                }
            );
            CheckNoThrow(
                OwnerDotnet,
                "ManualResetEvent construct/dispose",
                () =>
                {
                    using (var mre = new ManualResetEvent(false)) { }
                }
            );
        }

        private void ProbeTypeIdentity()
        {
            Section("Type identity");
            CheckProbe(
                OwnerDotnet,
                "typeof(string).FullName",
                "System.String",
                () => typeof(string).FullName
            );
            CheckProbe(
                OwnerDotnet,
                "typeof(int).FullName",
                "System.Int32",
                () => typeof(int).FullName
            );
            // .NET 10 embeds System.Private.CoreLib in generic type names
            // (mscorlib on .NET Framework).
            CheckProbe(
                OwnerDotnet,
                "List<int>.FullName names CoreLib",
                true,
                () => typeof(List<int>).FullName.Contains("System.Private.CoreLib")
            );
            CheckProbe(
                OwnerDotnet,
                "List<int>.ToString()",
                "System.Collections.Generic.List`1[System.Int32]",
                () => typeof(List<int>).ToString()
            );
            TryInfo("typeof(CultureInfo).FullName", () => typeof(CultureInfo).FullName);
        }

        private void ProbeSerializeXml()
        {
            Section("SerializeToXML / SerializeFromXML");
            var utils = MyAPIGateway.Utilities;

            string xml = null;
            try
            {
                xml = utils.SerializeToXML<int>(42);
            }
            catch (Exception ex)
            {
                xml = null;
                CheckTrue(
                    OwnerLinux,
                    "SerializeToXML(42) works",
                    false,
                    "<EXCEPTION: " + ex.GetType().Name + ": " + ex.Message + ">"
                );
            }
            if (xml != null)
            {
                Info("SerializeToXML(42) full text", xml);
                // Hex of the first characters exposes declaration + newline
                // handling at byte level (defect: length-only logging).
                var head = new StringBuilder();
                int n = Math.Min(xml.Length, 48);
                for (int i = 0; i < n; i++)
                {
                    if (i > 0)
                        head.Append(' ');
                    head.Append(((int)xml[i]).ToString("X2"));
                }
                Info("SerializeToXML(42) first chars hex", head.ToString());
                CheckTrue(
                    OwnerLinux,
                    "XML declaration is utf-16",
                    xml.StartsWith("<?xml version=\"1.0\" encoding=\"utf-16\"?>"),
                    xml
                );
                CheckTrue(
                    OwnerLinux,
                    "XML uses CRLF line breaks",
                    xml.IndexOf("\r\n", StringComparison.Ordinal) >= 0
                        && xml.Replace("\r\n", "").IndexOf('\n') < 0,
                    xml
                );
            }
            CheckProbe(
                OwnerLinux,
                "SerializeFromXML round-trip",
                42,
                () => utils.SerializeFromXML<int>(utils.SerializeToXML<int>(42))
            );
        }

        private void ProbeSerializeBinary()
        {
            Section("SerializeToBinary / SerializeFromBinary");
            var utils = MyAPIGateway.Utilities;
            CheckProbe(
                OwnerLinux,
                "binary serialize round-trip",
                "hello",
                () => utils.SerializeFromBinary<string>(utils.SerializeToBinary<string>("hello"))
            );
            TryInfo(
                "SerializeToBinary(hello) bytes",
                () => HexBytes(utils.SerializeToBinary<string>("hello"))
            );
        }

        private void ProbeSessionVariables()
        {
            Section("Session variables (Set/Get/Remove)");
            var utils = MyAPIGateway.Utilities;
            const string varName = "lcd.diag.var.probe";
            CheckNoThrow(OwnerLinux, "SetVariable", () => utils.SetVariable<int>(varName, 12345));
            CheckProbe(
                OwnerLinux,
                "GetVariable round-trip",
                12345,
                () =>
                {
                    int value;
                    return utils.GetVariable<int>(varName, out value) ? (object)value : "<not set>";
                }
            );
            CheckProbe(OwnerLinux, "RemoveVariable", true, () => utils.RemoveVariable(varName));
            CheckProbe(
                OwnerLinux,
                "GetVariable after remove",
                false,
                () =>
                {
                    int value;
                    return utils.GetVariable<int>(varName, out value);
                }
            );
        }

        private void ProbeOtherUtilities()
        {
            Section("Other IMyUtilities forwarders");
            var utils = MyAPIGateway.Utilities;
            CheckProbe(
                OwnerLinux,
                "GetTypeName(session component)",
                "LinuxCompatDiagnosticsSession",
                () => utils.GetTypeName(typeof(LinuxCompatDiagnosticsSession))
            );
            CheckProbe(
                OwnerLinux,
                "GetTypeName(string)",
                "String",
                () => utils.GetTypeName(typeof(string))
            );
            CheckProbe(
                OwnerLinux,
                "CreateNotification text round-trip",
                "probe",
                () =>
                {
                    var n = utils.CreateNotification("probe", 1, "White");
                    return n == null ? null : n.Text;
                }
            );
        }

        private void ProbeMessagingSetup()
        {
            Section("Mod messaging and InvokeOnGameThread (async, verified on update)");
            var utils = MyAPIGateway.Utilities;

            CheckNoThrow(
                OwnerLinux,
                "RegisterMessageHandler",
                () =>
                {
                    _modMessageHandler = HandleModMessage;
                    utils.RegisterMessageHandler(ModMessageChannelId, _modMessageHandler);
                }
            );
            CheckNoThrow(
                OwnerLinux,
                "SendModMessage",
                () =>
                    utils.SendModMessage(
                        ModMessageChannelId,
                        "LinuxCompatDiagnostics probe payload"
                    )
            );
            CheckNoThrow(
                OwnerLinux,
                "InvokeOnGameThread scheduling",
                () => utils.InvokeOnGameThread(CountInvokeOnGameThread, "LinuxCompatDiagnostics")
            );
        }

        private void HandleModMessage(object payload)
        {
            Interlocked.Increment(ref _modMessageReceivedCount);
            _modMessageLastPayload = payload;
        }

        private void CountInvokeOnGameThread()
        {
            Interlocked.Increment(ref _invokeOnGameThreadCount);
        }
    }
}
