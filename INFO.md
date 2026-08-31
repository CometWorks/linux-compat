# Trade station icon path bug

The trade station logs contained 116 missing icon errors from the Colorful Icons mod. Every failed path had the same malformed prefix:

```text
/SpaceEngineers/Content/C:/users/steamuser/.local/share/Steam/steamapps/workshop/...
```

There is no literal `C:` directory on Linux. Proton represents its Windows C drive on disk as `pfx/drive_c`, but Linux Compat does not route native game file access through that directory. Instead, it exposes synthetic Windows paths such as `C:\users\steamuser\...` to mods so they continue to see Windows path semantics.

Colorful Icons appended its relative icon paths to the synthetic mod path and stored the results in shared game definitions. Those values reached the native renderer without being translated back to Linux paths. On Linux, .NET does not consider `C:\...` rooted, so the renderer treated it as relative and combined it with `ContentPath`. This produced the invalid `Content/C:/...` paths in the logs.

The `C:` prefix is only part of the mod-facing compatibility namespace. It should never reach native game file access and should not be mapped directly to Proton's `drive_c`. At the mod API boundary it must be restored to the original Linux path, such as `/home/space/.local/share/Steam/...`.

`PathCache.ResolveAbsolute` is the shared-data path translation funnel. It now detects a synthetic drive path accidentally embedded under `ContentPath`, removes the content prefix, and translates the synthetic path back to its native Linux root before casing resolution and file access. This fixes all affected icons without adding icon-specific replacements.
