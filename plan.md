# WeaponCore Crash Findings

## Summary

WeaponCore is crashing itself during platform initialization. The observed failure is not a CLR/runtime exception in the main game log. WeaponCore's own log reports that the `AutoCannonTurret` platform is invalid because the expected muzzle/elevation subpart `MissileTurretBarrels` is missing.

The most likely root cause is game-side mod definition/model path resolution on Linux: the autocannon turret's parent model path is not being resolved as a mod-local absolute path before model/subpart loading. Because nested subpart paths are derived from the parent model path, the game falls back to the vanilla `Content` tree and cannot load WeaponCore's mod-local model/subparts consistently.

This should be fixed in the existing game-side definition/path compatibility patches, not in the Roslyn mod-source rewriter.

## Evidence

World:

`/home/space/.config/SpaceEngineers/Saves/76561198317912469/WC Crash Repro/`

WeaponCore storage:

`/home/space/.config/SpaceEngineers/Storage/3154371364.sbm_CoreSystems`

WeaponCore source actually present at:

`/home/space/.local/share/Steam/steamapps/workshop/content/244850/3154371364/`

The originally supplied path does not exist on this machine:

`/home/space/.steam/steam/steamapps/workshop/content/244850/3154371364/`

WeaponCore log:

`/home/space/.config/SpaceEngineers/Storage/3154371364.sbm_CoreSystems/debug.log`

```text
05-24-26_09-44-33-085 - PlatformCrash: AutoCannonTurret - Your block subTypeId (AutoCannonTurret) Weapon: Autocannon Turret Invalid muzzlePart, I am crashing now Dave. MissileTurretBarrels was not found.  Ensure you do not include subpart_ in the Id fields in the weapon definition
05-24-26_09-44-33-085 - PlatformCrash: AutoCannonTurret - Platform PreInit is in an invalid state: AutoCannonTurret
```

Save-local WeaponCore log shows the same prior repro:

`/home/space/.config/SpaceEngineers/Saves/76561198317912469/WC Crash Repro/debug.log`

```text
05-20-26_18-55-25-344 - PlatformCrash: AutoCannonTurret - Your block subTypeId (AutoCannonTurret) Weapon: Autocannon Turret Invalid muzzlePart, I am crashing now Dave. MissileTurretBarrels was not found.  Ensure you do not include subpart_ in the Id fields in the weapon definition
```

Main game log confirms the mod/session load reaches WeaponCore script and definitions before the WeaponCore platform crash:

`/home/space/.config/SpaceEngineers/SpaceEngineers_20260524_094224403.log`

Relevant entries:

```text
Script loaded: 3154371364.sbm_CoreSystems, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
Loading cube block: MyObjectBuilder_LargeMissileTurret/AutoCannonTurret
Session loaded
```

Render log indicates the autocannon turret model lookup fell through to the game content root and failed:

`/home/space/.config/SpaceEngineers/VRageRender-DirectX11_20260524_094224563.log`

```text
Mesh asset /mnt/steamone/SteamLibrary/steamapps/common/SpaceEngineers/Content/Models/Cubes/small/AutocannonTurret_Base.mwm missing
```

That path does not exist on this machine. The real game install is under:

`/home/space/.local/share/Steam/steamapps/common/SpaceEngineers`

## WeaponCore Data Details

WeaponCore's active override for the autocannon turret is:

`/home/space/.local/share/Steam/steamapps/workshop/content/244850/3154371364/Data/CubeBlocks_Weapons.sbc`

It defines an enabled:

`MyObjectBuilder_LargeMissileTurret/AutoCannonTurret`

with:

```xml
<Model>Models\Cubes\Small\AutocannonTurret_Base.mwm</Model>
<WeaponDefinitionId Subtype="AutocannonTurret" />
<MuzzleProjectileDummyName>muzzle_projectile</MuzzleProjectileDummyName>
```

The vanilla game definition for `LargeGatlingTurret/AutoCannonTurret` includes a `SubpartPairing` that maps the vanilla autocannon model names:

```xml
<SubpartPairing>
  <dictionary>
    <item>
      <Key>Base1</Key>
      <Value>AutocannonTurret_Base1</Value>
    </item>
    <item>
      <Key>Base2</Key>
      <Value>AutocannonTurret_Base1/AutocannonTurret_Barrel</Value>
    </item>
    <item>
      <Key>Barrel</Key>
      <Value>AutocannonTurret_Base1/AutocannonTurret_Barrel</Value>
    </item>
  </dictionary>
</SubpartPairing>
<MuzzleProjectileDummyName>muzzle_missile_001</MuzzleProjectileDummyName>
```

WeaponCore's override has similar `SubpartPairing` blocks, but they are commented out. That looks intentional because WeaponCore ships models whose dummy/subpart names are missile-style.

WeaponCore's C# weapon definition is:

`/home/space/.local/share/Steam/steamapps/workshop/content/244850/3154371364/Data/Scripts/CoreSystems/Coreparts/Definitions/Autocannons.cs`

Relevant fields:

```csharp
WeaponDefinition AutoCannonTurret => new WeaponDefinition
{
    Assignments = new ModelAssignmentsDef
    {
        MountPoints = new[] {
            new MountPointDef {
                SubtypeId = "AutoCannonTurret",
                SpinPartId = "None",
                MuzzlePartId = "MissileTurretBarrels",
                AzimuthPartId = "MissileTurretBase1",
                ElevationPartId = "MissileTurretBarrels",
            },
        },
        Muzzles = new[] {
            "muzzle_missile_01",
        },
    },
};
```

WeaponCore's recursive subpart discovery is in:

`/home/space/.local/share/Steam/steamapps/workshop/content/244850/3154371364/Data/Scripts/CoreSystems/EntityComp/ModelSupport/RecursiveSubparts.cs`

Relevant behavior:

```csharp
if (kv.Key.StartsWith("subpart_", StringComparison.Ordinal))
{
    var name = kv.Key.Substring("subpart_".Length);
    MyEntitySubpart res;
    if (query.TryGetSubpart(name, out res))
    {
        _subparts.Add(res);
        NameToEntity[name] = res;
        EntityToName[res] = name;
    }
}
```

WeaponCore's platform validation then fails here:

`/home/space/.local/share/Steam/steamapps/workshop/content/244850/3154371364/Data/Scripts/CoreSystems/EntityComp/PlatformInit.cs`

Relevant behavior:

```csharp
var muzzlePartName = system.MuzzlePartName.String != "Designator" ? system.MuzzlePartName.String : system.ElevationPartName.String;

if (!Parts.NameToEntity.TryGetValue(muzzlePartName, out muzzlePartEntity))
{
    return PlatformCrash(Comp, true, true, $"Your block subTypeId ({Comp.SubtypeName}) Weapon: {system.PartName} Invalid muzzlePart, I am crashing now Dave. {muzzlePartName} was not found.  Ensure you do not include subpart_ in the Id fields in the weapon definition");
}
```

## Model Inspection

The WeaponCore mod-local models contain the exact missile-style names expected by WeaponCore's C# definition.

`AutocannonTurret_Base.mwm` contains:

```text
subpart_MissileTurretBase1
AutocannonTurret_Base1
AutocannonTurret_Base
```

`AutocannonTurret_Base1.mwm` contains:

```text
subpart_MissileTurretBarrels
AutocannonTurret_Barrel
AutocannonTurret_Base1
```

`AutocannonTurret_Barrel.mwm` contains:

```text
muzzle_missile_01
AutocannonTurret_Barrel
```

Vanilla game autocannon models use vanilla autocannon-style subpart names instead:

`Content/Models/Cubes/small/AutocannonTurret_Base.mwm` contains:

```text
subpart_AutocannonTurret_Base1
```

`Content/Models/Cubes/small/AutocannonTurret_Base1.mwm` contains:

```text
subpart_AutocannonTurret_Barrel
```

`Content/Models/Cubes/small/AutocannonTurret_Barrel.mwm` contains:

```text
muzzle_projectile
```

This means rewriting WeaponCore's C# constants from `MissileTurretBarrels` to vanilla autocannon names would be wrong for WeaponCore's own shipped models. The mod-local models intentionally match the missile-style definition.

## Game Code Relationship

The game creates subparts from model dummy custom data in:

`/home/space/Documents/dotnet-game-local/VRage.Game/VRage/Game/Entity/MyEntitySubpart.cs`

Relevant behavior:

```csharp
string text = Path.Combine(Path.GetDirectoryName(PathUtils.Normalize(modelPath)), (string)dummy.CustomData["file"]);
text += ".mwm";
outData = new Data
{
    Name = dummyName.Substring("subpart_".Length),
    File = text,
    InitialTransform = Matrix.Normalize(dummy.Matrix)
};
```

Nested subpart paths are derived from the parent `modelPath`. If a mod-local parent model remains content-relative instead of becoming an absolute mod-rooted path, nested subparts are derived relative to the game content path instead of the mod folder.

That matches the observed render log: `AutocannonTurret_Base.mwm` is being searched under game `Content`, not under WeaponCore's workshop folder.

## Current Compat Plugin Path Fixes

The current plugin already contains multiple non-rewriter path fixes related to definitions and assets under:

`ClientPlugin/Patches/PathHandling/`

Most relevant:

- `MyDefinitionManagerPatch.cs`
- `ModelPathPatch.cs`
- `MwmUtilsPatch.cs`
- `LODDescriptorPatch.cs`
- `MyFileSystemPatch.cs`
- `MyFileSystemOpenPrepatch.cs`
- `GuiTextureAtlasDefinitionPatch.cs`

`MyDefinitionManagerPatch.cs` is the primary existing definition-side patch. It patches:

- `MyDefinitionManager.LoadDefinitions`
- `MyDefinitionManager.ProcessContentFilePath`
- `MyDefinitionManager.CreateTransparentMaterials`

The `ProcessContentFilePath` patch currently normalizes definition asset paths, performs case-insensitive extension checks, tries resolving under `context.ModPath`, and falls back to game `Content`.

Current relevant code:

```csharp
contentFile = PathHelpers.Normalize(contentFile);
string extension = Path.GetExtension(contentFile);

string resolved = CaseInsensitivePathResolver.Resolve(contentFile, context.ModPath);
if (!MyDefinitionManager.m_directoryExistCache.TryGetValue(resolved, out var exists))
{
    exists = MyFileSystem.DirectoryExists(Path.GetDirectoryName(resolved))
          && System.Linq.Enumerable.Any(MyFileSystem.GetFiles(
                Path.GetDirectoryName(resolved),
                Path.GetFileName(resolved),
                MySearchOption.TopDirectoryOnly));
    MyDefinitionManager.m_directoryExistCache.Add(resolved, exists);
}

if (exists)
{
    contentFile = resolved;
}
else if (!MyFileSystem.FileExists(PathHelpers.ResolveContentFilePath(contentFile, MyFileSystem.ContentPath)))
{
    if (contentFile.EndsWith(".mwm"))
    {
        MyDefinitionErrors.Add(context, "Resource not found, setting to error model. Resource path: " + resolved, TErrorSeverity.Error);
        contentFile = "Models/Debug/Error.mwm";
    }
    else
    {
        MyDefinitionErrors.Add(context, "Resource not found, setting to null. Resource path: " + resolved, TErrorSeverity.Error);
        contentFile = null;
    }
}
```

This is the best place to fix the WeaponCore problem if `contentFile` is not ending up as a mod-rooted absolute path for the autocannon model.

## Why Game-Side Definition Fix Is Preferred

This is more suitable as a game-side definition loading fix than a Roslyn mod-source rewrite.

Reasons:

- `MyObjectBuilder_CubeBlockDefinition.Model` and `BuildProgressModel.File` are annotated with `[ModdableContentFile("mwm")]` in the game code.
- The game already routes these fields through `MyDefinitionManager.ProcessContentFilePath()` before initializing definitions.
- Fixing the path there preserves mod intent and applies to all mods, not only WeaponCore.
- It fixes the parent model path before `MyEntitySubpart.GetSubpartFromDummy()` derives nested subpart paths.
- It avoids brittle, mod-specific C# source rewrites.
- It avoids changing WeaponCore's valid missile-style subpart constants, which match its shipped models.

The current Roslyn mod rewriter is intended for mod source code platform semantics such as `System.IO.Path`, `Environment.NewLine`, `Stopwatch`, `TextWriter.WriteLine`, and `ModItem.GetPath()`. It does not see SBC/XML definition files and is too late for definition model resolution.

## Recommended Fix Direction

Treat this as a bug in `PathCache.Resolve(relativePath, rootPath)`, not as a new mod-source rewriter feature and not as a `MyDefinitionManagerProcessContentFilePathPatch` special case.

Recommended steps:

1. Add targeted diagnostics inside `MyDefinitionManagerProcessContentFilePathPatch` for non-base-game `.mwm` paths.

2. Log the following for WeaponCore's `AutocannonTurret_Base.mwm`:

   - `context.ModName`
   - `context.ModPath`
   - incoming `contentFile`
   - normalized `contentFile`
   - mod-local `resolved`
   - whether `File.Exists(resolved)` is true
   - whether `MyFileSystem.FileExists(resolved)` is true
   - fallback content path candidate

3. Ensure `PathCache.Resolve(relativePath, rootPath)` treats a non-empty `rootPath` as authoritative. It must combine the relative path with `rootPath` before probing any root-relative static content cache entry.

4. Keep root-relative static cache lookups only for calls where no explicit root is provided. The current behavior makes `rootPath` advisory and lets vanilla `Content` win over a mod root when the same relative asset path exists in both places.

5. `MyDefinitionManagerProcessContentFilePathPatch` can continue calling `CaseInsensitivePathResolver.Resolve(contentFile, context.ModPath)`. Once the resolver is fixed, that call should return the mod-rooted absolute path when the mod-local file exists.

6. Keep the lower-level patches as safety nets:

   - `ModelPathPatch.cs` for `MyModelImporter.ImportData`
   - `MwmUtilsPatch.cs` for render-side MWM path casing
   - `LODDescriptorPatch.cs` for LOD paths
   - `MyFileSystemPatch.cs` / `MyFileSystemOpenPrepatch.cs` for generic FS calls

7. Verify with the repro world:

   - WeaponCore `debug.log` no longer contains `PlatformCrash: AutoCannonTurret`.
   - Render log no longer reports `AutocannonTurret_Base.mwm missing` under game `Content`.
   - WeaponCore discovers `MissileTurretBase1` and `MissileTurretBarrels`.
   - Session loads without the WeaponCore platform invalid state.

## Latest Run Result

The latest repro confirmed the suspected gap in the current implementation:

- `SpaceEngineers_20260524_101250910.log:739` shows `AutocannonTurret_Base.mwm` being processed with the correct `modPath`, but `resolved` still points at game `Content`:
  - `resolved=/home/space/.steam/steam/steamapps/common/SpaceEngineers/Content/Models/Cubes/small/AutocannonTurret_Base.mwm`
- That means the current mod-local resolution path is being short-circuited by the static content cache before the mod-rooted candidate wins.
- The mod-local file does exist at:
  - `/home/space/.local/share/Steam/steamapps/workshop/content/244850/3154371364/Models/Cubes/Small/AutocannonTurret_Base.mwm`
- WeaponCore still crashes:
  - `Storage/3154371364.sbm_CoreSystems/debug.log:3`
- Render still reports the vanilla-content lookup miss:
  - `VRageRender-DirectX11_20260524_10125151.log:545`

Conclusion: `PathCache.Resolve` is applying the static root-relative content cache before honoring the caller's explicit root. The resolver itself should be fixed so every caller that supplies a root gets root-scoped resolution.

## Potential Implementation Shape

The patch should be minimal and centered on `PathCache.Resolve`.

Conceptual behavior:

```csharp
relativePath = PathHelpers.Normalize(relativePath);
rootPath = PathHelpers.Normalize(rootPath);

if (Path.IsPathRooted(relativePath))
    return ResolveAbsolute(relativePath);

if (!string.IsNullOrEmpty(rootPath))
{
    var fullPath = Path.Combine(rootPath, relativePath).Replace('\\', '/');
    return Path.IsPathRooted(fullPath) ? ResolveAbsolute(fullPath) : fullPath;
}

// Only rootless calls may use the global Content/Bin64 root-relative cache.
```

This preserves `PathHelpers.ResolveContentFilePath(contentFile, MyFileSystem.ContentPath)` because `MyFileSystem.ContentPath` is an explicit root and resolves via the absolute content path. It also fixes mod-location APIs that currently risk resolving a mod-relative path to vanilla content.

Implemented in `ClientPlugin/Patches/PathHandling/PathHelpers.cs`: `PathCache.Resolve` now combines a relative path with a non-empty `rootPath` before consulting root-relative static cache entries.

## Open Questions

- Does the fixed resolver return the WeaponCore mod-local `AutocannonTurret_Base.mwm` path in a fresh repro run?
- Does the fixed resolver preserve content-root lookups through `PathHelpers.ResolveContentFilePath(..., MyFileSystem.ContentPath)`?
- Are there duplicate WeaponCore definitions where the active override bypasses `UpdateModableContent` or uses a builder field not annotated with `[ModdableContentFile]`?

These should be answered with a fresh game run after the resolver change.
