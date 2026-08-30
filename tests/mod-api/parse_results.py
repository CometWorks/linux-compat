#!/usr/bin/env python3
"""Parse a LinuxCompatDiagnostics suite log and report pass/fail.

Usage:
  parse_results.py <LinuxCompatDiagnostics.log> [--security-manifest FILE]
  parse_results.py <LinuxCompatDiagnostics.log> --update-security-manifest FILE

Exit codes:
  0 - complete run, no FAIL lines, security manifest satisfied
  1 - complete run with FAIL lines, or a missing security probe
  2 - incomplete or missing log (no END terminator)

Failures are reported in three sections:
  security - path-containment probes (named "security: ..."); a FAIL here is a
             boundary escape or an over-tightened check, and is reported first
             regardless of owner tag
  [LINUX]  - linux-compat bugs (Windows-semantics expectations broken)
  [DOTNET] - follow-up items for the se-dotnet-compat repo (raw .NET 10
             expectations broken; may indicate an active dotnet-compat shim)

The security manifest is the regression net for the containment checks: it
lists every security probe name a green run is expected to report. A probe that
disappears - because the probe was deleted, renamed, or never ran since the
suite died early in that section - fails the run even though no FAIL line
exists for it. Regenerate it from a known-good run with
--update-security-manifest and commit the result.
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
SECURITY_PREFIX = "security: "


def read_manifest(path):
    names = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line and not line.startswith("#"):
                names.append(line)
    return names


def write_manifest(path, names):
    with open(path, "w", encoding="utf-8") as f:
        f.write("# Security probe manifest for the LinuxCompatDiagnostics suite.\n")
        f.write("# Every name below must be reported by a green run; a missing one\n")
        f.write("# fails parse_results.py even when no probe FAILs. Regenerate with\n")
        f.write("#   parse_results.py <log> --update-security-manifest <this file>\n")
        for name in names:
            f.write(name + "\n")


def print_failures(title, rows):
    print()
    print(f"{title} ({len(rows)}):")
    for _, _, name, expected, actual in rows:
        print(f"  FAIL {name}")
        print(f"       expected: {expected}")
        print(f"       actual:   {actual}")


def main() -> int:
    args = sys.argv[1:]
    manifest_path = None
    update_manifest = None

    i = 0
    positional = []
    while i < len(args):
        if args[i] == "--security-manifest" and i + 1 < len(args):
            manifest_path = args[i + 1]
            i += 2
        elif args[i] == "--update-security-manifest" and i + 1 < len(args):
            update_manifest = args[i + 1]
            i += 2
        else:
            positional.append(args[i])
            i += 1

    if len(positional) != 1:
        print(__doc__, file=sys.stderr)
        return 2

    path = positional[0]
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
    security = [r for r in results if r[2].startswith(SECURITY_PREFIX)]
    security_failed = [r for r in security if r[0] == "FAIL"]
    security_names = [r[2] for r in security]
    # Security failures are reported in their own section, not twice.
    linux_failed = [
        r for r in failed if r[1] == "[LINUX]" and not r[2].startswith(SECURITY_PREFIX)
    ]
    dotnet_failed = [
        r for r in failed if r[1] == "[DOTNET]" and not r[2].startswith(SECURITY_PREFIX)
    ]

    if update_manifest:
        write_manifest(update_manifest, sorted(set(security_names)))
        print(f"wrote {len(set(security_names))} security probe names to {update_manifest}")

    print(f"Suite log: {path}")
    print(f"Probes: {len(results)} total, {len(passed)} passed, {len(failed)} failed")
    print(
        f"Security probes: {len(security)} "
        f"({len(security) - len(security_failed)} passed, {len(security_failed)} failed) - "
        f"{sum(1 for n in security_names if ' refused, ' in n)} refusal, "
        f"{sum(1 for n in security_names if ' allowed, ' in n)} legal-access"
    )

    missing = []
    unknown = []
    if manifest_path:
        try:
            expected_names = read_manifest(manifest_path)
        except OSError as ex:
            print(f"ERROR: cannot read security manifest {manifest_path}: {ex}", file=sys.stderr)
            return 2
        reported = set(security_names)
        missing = [n for n in expected_names if n not in reported]
        unknown = sorted(reported - set(expected_names))

    if security_failed:
        print_failures("SECURITY failures - containment escape or over-tightened check", security_failed)

    if missing:
        print()
        print(f"SECURITY probes missing from the run ({len(missing)}) - "
              "deleted, renamed, or the suite died before reporting them:")
        for name in missing:
            print(f"  MISSING {name}")

    if unknown:
        print()
        print(f"New security probes not in the manifest ({len(unknown)}) - "
              "re-run with --update-security-manifest and commit it:")
        for name in unknown:
            print(f"  NEW {name}")

    if linux_failed:
        print_failures("[LINUX] failures - linux-compat bugs", linux_failed)

    if dotnet_failed:
        print_failures("[DOTNET] failures - follow-ups for se-dotnet-compat", dotnet_failed)

    if fatal:
        print()
        print("FATAL lines in the log:")
        for line in fatal:
            print(f"  {line}")

    print()
    if not complete:
        print("RESULT: INCOMPLETE - the END terminator is missing; the suite died mid-run.")
        return 2
    if not security:
        print("RESULT: FAIL - the suite reported no security probes at all.")
        return 1
    if summary is not None:
        s_pass, s_fail, s_linux, s_dotnet, s_info = summary
        parsed_linux = len([r for r in failed if r[1] == "[LINUX]"])
        parsed_dotnet = len([r for r in failed if r[1] == "[DOTNET]"])
        if (s_pass, s_linux, s_dotnet) != (len(passed), parsed_linux, parsed_dotnet):
            print(
                "WARNING: summary line disagrees with parsed lines "
                f"(summary pass={s_pass} linux_fail={s_linux} dotnet_fail={s_dotnet})"
            )
    if failed or fatal or missing:
        parts = [f"{len(security_failed)} security", f"{len(linux_failed)} linux",
                 f"{len(dotnet_failed)} dotnet"]
        if missing:
            parts.append(f"{len(missing)} missing security probes")
        if fatal:
            parts.append(f"{len(fatal)} fatal")
        print("RESULT: FAIL (" + ", ".join(parts) + ")")
        return 1
    print(f"RESULT: PASS ({len(security)} security probes included)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
