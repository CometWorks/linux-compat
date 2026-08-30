#!/usr/bin/env python3
"""Parse a LinuxCompatDiagnostics suite log and report pass/fail.

Usage: parse_results.py <LinuxCompatDiagnostics.log>

Exit codes:
  0 - complete run, no FAIL lines
  1 - complete run with FAIL lines
  2 - incomplete or missing log (no END terminator)

Failures are reported in two sections by owner tag:
  [LINUX]  - linux-compat bugs (Windows-semantics expectations broken)
  [DOTNET] - follow-up items for the se-dotnet-compat repo (raw .NET 10
             expectations broken; may indicate an active dotnet-compat shim)
"""

import re
import sys

RESULT_RE = re.compile(
    r"^(PASS|FAIL) (\[LINUX\]|\[DOTNET\]) (.*?) \| expected=(.*) \| actual=(.*)$"
)
SUMMARY_RE = re.compile(
    r"^=== SUMMARY pass=(\d+) fail=(\d+) linux_fail=(\d+) dotnet_fail=(\d+) info=(\d+) ===$"
)
END_MARKER = "=== END LinuxCompatDiagnostics ==="


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__, file=sys.stderr)
        return 2

    path = sys.argv[1]
    try:
        with open(path, encoding="utf-8", errors="replace") as f:
            lines = [line.rstrip("\r\n") for line in f]
    except OSError as ex:
        print(f"ERROR: cannot read {path}: {ex}", file=sys.stderr)
        return 2

    results = []  # (status, owner, name, expected, actual)
    summary = None
    fatal = [line for line in lines if line.startswith("FATAL ")]
    complete = any(line == END_MARKER for line in lines)

    for line in lines:
        m = RESULT_RE.match(line)
        if m:
            results.append(m.groups())
            continue
        m = SUMMARY_RE.match(line)
        if m:
            summary = tuple(int(g) for g in m.groups())

    passed = [r for r in results if r[0] == "PASS"]
    failed = [r for r in results if r[0] == "FAIL"]
    linux_failed = [r for r in failed if r[1] == "[LINUX]"]
    dotnet_failed = [r for r in failed if r[1] == "[DOTNET]"]

    print(f"Suite log: {path}")
    print(f"Probes: {len(results)} total, {len(passed)} passed, {len(failed)} failed")

    if linux_failed:
        print()
        print(f"[LINUX] failures ({len(linux_failed)}) - linux-compat bugs:")
        for _, _, name, expected, actual in linux_failed:
            print(f"  FAIL {name}")
            print(f"       expected: {expected}")
            print(f"       actual:   {actual}")

    if dotnet_failed:
        print()
        print(f"[DOTNET] failures ({len(dotnet_failed)}) - follow-ups for se-dotnet-compat:")
        for _, _, name, expected, actual in dotnet_failed:
            print(f"  FAIL {name}")
            print(f"       expected: {expected}")
            print(f"       actual:   {actual}")

    if fatal:
        print()
        print("FATAL lines in the log:")
        for line in fatal:
            print(f"  {line}")

    print()
    if not complete:
        print("RESULT: INCOMPLETE - the END terminator is missing; the suite died mid-run.")
        return 2
    if summary is not None:
        s_pass, s_fail, s_linux, s_dotnet, s_info = summary
        if (s_pass, s_linux, s_dotnet) != (len(passed), len(linux_failed), len(dotnet_failed)):
            print(
                "WARNING: summary line disagrees with parsed lines "
                f"(summary pass={s_pass} linux_fail={s_linux} dotnet_fail={s_dotnet})"
            )
    if failed or fatal:
        print(f"RESULT: FAIL ({len(linux_failed)} linux, {len(dotnet_failed)} dotnet"
              + (f", {len(fatal)} fatal" if fatal else "") + ")")
        return 1
    print("RESULT: PASS")
    return 0


if __name__ == "__main__":
    sys.exit(main())
