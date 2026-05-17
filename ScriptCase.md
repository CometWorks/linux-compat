# Case-Insensitive Local Ingame Script Paths

## Problem

Local ingame scripts under `~/.config/SpaceEngineers/IngameScripts/local` are filtered and opened through hardcoded `Script.cs` probes. On Linux, `script.cs` and `Script.cs` are distinct paths, so scripts generated with lowercase `script.cs` are hidden or fail to open.

Expected local script layout:

```text
~/.config/SpaceEngineers/IngameScripts/local/<ScriptName>/Script.cs
```

Current failure mode:

```text
~/.config/SpaceEngineers/IngameScripts/local/<ScriptName>/script.cs
```

## Relevant Game Code

Reference source: `/home/space/Documents/dotnet-game-local`

- `Sandbox.Game/Sandbox/Game/Gui/MyGuiBlueprintScreen_Reworked.cs`
- `GetLocalNames_Scripts` enumerates local script directories and calls `MyBlueprintUtils.IsItem_Script(text)` before adding an entry.
- `OpenSelectedSript` builds the selected local script path with `MyBlueprintUtils.DEFAULT_SCRIPT_NAME + MyBlueprintUtils.SCRIPT_EXTENSION`, which resolves to `Script.cs`.
- `Sandbox.Game/Sandbox/Game/Gui/MyBlueprintUtils.cs`
- `IsItem_Script` checks for `path + "\\Script.cs"` in the stock game.
- `Sandbox.Game/Sandbox/Game/Gui/MyGuiScreenEditor.cs`
- `ScriptSelected` directly uses `File.Exists(scriptPath)` and `File.ReadAllText(scriptPath)` when the selected path has a `.cs` extension.
- `Sandbox.Game/Sandbox/Game/Gui/MyGuiIngameScriptsPage.cs`
- `OnReplaceFromEditor` directly builds `<local>/<ScriptName>/Script.cs`, then uses `File.Exists` and `File.WriteAllText`.
- `Sandbox.Game/Sandbox/Game/Gui/MyGuiBlueprintScreen_Reworked.cs`
- `OnButton_Replace` directly builds `SCRIPT_FOLDER_LOCAL/<ScriptName>/Script.cs`, then uses `File.Exists` and `File.WriteAllText`.
- In the reworked screen this replace path also omits `GetCurrentLocalDirectory()`, unlike the open/delete/create paths.

## Existing Compat Context

- `ClientPlugin/Patches/PathHandling/MyBlueprintUtilsIsItemPatch.cs` already replaces the stock backslash concatenation with `Path.Combine(path, "Script.cs")`.
- That fixes separators, but not Linux case sensitivity.
- `ClientPlugin/Patches/PathHandling/PathHelpers.cs` already provides the shared case-insensitive resolver via `PathCache.ResolveAbsolute`.
- `PathCache.ResolveAbsolute` covers mutable roots such as `MyFileSystem.UserDataPath`, so it is the correct mechanism for `IngameScripts/local`.

## Plan

1. Update `MyBlueprintUtilsIsItemScriptPatch`.
   - Build the canonical probe path with `Path.Combine(path, "Script.cs")`.
   - Pass it through `PathCache.ResolveAbsolute` before `File.Exists`.
   - This lets `script.cs`, `SCRIPT.CS`, or other case variants satisfy the script-directory filter while returning false for genuinely missing scripts.

2. Consider applying the same resolver to `MyBlueprintUtilsIsItemBlueprintPatch`.
   - This is not required for the reported script bug.
   - It would keep blueprint `bp.sbc` probing consistent with Windows-style case-insensitive behavior.
   - If included, resolve `Path.Combine(path, "bp.sbc")` before `File.Exists`.

3. Patch `MyGuiScreenEditor.ScriptSelected` for direct file opens.
   - Add a Harmony prefix or transpiler that resolves the `scriptPath` argument with `PathCache.ResolveAbsolute` before the original method runs.
   - This is needed because local script selection passes a hardcoded `.../Script.cs` path to the editor.
   - The method uses raw `System.IO.File.Exists` and `File.ReadAllText`, bypassing `MyFileSystem` path patches.

4. Keep the patch narrow.
    - Do not globally patch `System.IO.File.Exists` or `File.ReadAllText`.
    - Do not globally patch `System.IO.File.WriteAllText`.
    - Do not introduce a second script-specific resolver.
    - Reuse `PathCache.ResolveAbsolute` so behavior matches other Linux path fixes in the plugin.

5. Patch replace-button paths.
   - Add a narrow helper that resolves the canonical local script file path through `PathCache.ResolveAbsolute`.
   - Patch `MyGuiIngameScriptsPage.OnReplaceFromEditor` to preserve the confirmation dialog, but write through the resolved path.
   - Patch `MyGuiBlueprintScreen_Reworked.OnButton_Replace` only for local script replacement.
   - Include `GetCurrentLocalDirectory()` in the reworked local script path before resolving, matching the open/delete/create code paths.
   - Leave blueprint and cloud replacement on the original game code path.

6. Verify with manual local script cases.
    - Create `~/.config/SpaceEngineers/IngameScripts/local/LowercaseScript/script.cs`.
    - Open the in-game scripts browser and confirm `LowercaseScript` appears.
    - Select it and confirm the editor loads the file contents.
    - Edit the code, click replace, confirm `script.cs` is overwritten and no new `Script.cs` sibling is created.
    - Repeat with the canonical `Script.cs` to confirm existing behavior is unchanged.

7. Verify build.
    - Run `dotnet build LinuxCompat.sln` from the repository root.
    - Check for Harmony patch binding errors in the game log after launch.

## Minimal Code Shape

```csharp
static bool Prefix(string path, ref bool __result)
{
    var scriptPath = Path.Combine(path, "Script.cs");
    scriptPath = PathCache.ResolveAbsolute(scriptPath);
    __result = File.Exists(scriptPath);
    return false;
}
```

For `MyGuiScreenEditor.ScriptSelected`, prefer a prefix if Harmony can bind the private method safely:

```csharp
[HarmonyPatch(typeof(MyGuiScreenEditor), "ScriptSelected")]
[HarmonyPatchCategory("Finish")]
static class MyGuiScreenEditorScriptSelectedPatch
{
    static void Prefix(ref string scriptPath)
    {
        if (!string.IsNullOrEmpty(scriptPath))
            scriptPath = PathCache.ResolveAbsolute(scriptPath);
    }
}
```
