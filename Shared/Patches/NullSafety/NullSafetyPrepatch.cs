using System;
using System.Collections.Generic;
using System.Linq;
using ClientPlugin.Tools;
using Mono.Cecil;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Weapons;

namespace ClientPlugin.Patches.NullSafety;

/// <summary>
/// Guards that swallow the exceptions Linux timing and missing model subparts cause in a few
/// game methods. They were Harmony finalizers, but a lost Harmony patch turns each of them back
/// into a crash (SE #1073), so they are written into the method bodies instead
/// (<see cref="PreloaderHelpers.WrapInExceptionFilter"/>).
/// </summary>
public static class NullSafetyPrepatch
{
    private static readonly (string Type, string Method, int Parameters, string Filter)[] Guards =
    [
        // Session init on Linux can race these; the game recomputes or retries later.
        ("Sandbox.Game.AI.MyAIComponent", "LoadData", 0, nameof(NullReference)),
        (
            "Sandbox.Game.Entities.Blocks.MyGasTank",
            "ComputeRequiredPower",
            0,
            nameof(NullReference)
        ),
        ("Sandbox.Game.Screens.Helpers.MyToolbarItemCubeBlock", "Init", 1, nameof(NullReference)),
        // Missing model subparts: retry on the next tick, or go on without the subpart.
        (
            "Sandbox.Game.Entities.Cube.MyLaserAntenna",
            "OnModelChange",
            0,
            nameof(LaserAntennaModelChange)
        ),
        (
            "Sandbox.Game.Weapons.MyAngleGrinder",
            "UpdateAfterSimulation",
            0,
            nameof(MissingSubpartOrNull)
        ),
        ("Sandbox.Game.Weapons.MyHandDrill", "Init", 1, nameof(KeyNotFound)),
        ("Sandbox.Game.Weapons.MyHandDrill", "UpdateAfterSimulation", 0, nameof(NullReference)),
        (
            "SpaceEngineers.Game.Entities.Weapons.MyLargeGatlingTurret",
            "OnModelChange",
            0,
            nameof(TurretModelChange)
        ),
    ];

    public static void Prepatch(AssemblyDefinition asmDef)
    {
        var module = asmDef.MainModule;
        foreach (var guard in Guards)
        {
            var type = module.GetType(guard.Type);
            if (type == null)
                continue; // in the other assembly
            var label = $"{type.Name}.{guard.Method}";
            var method = type.Methods.FirstOrDefault(m =>
                m.Name == guard.Method && m.Parameters.Count == guard.Parameters && m.HasBody
            );
            if (method == null)
            {
                Console.WriteLine($"[LinuxCompat] NullSafetyPrepatch: {label} not found");
                continue;
            }

            method.Body.Instructions.RecordOriginalCode(method);
            var filter = module.ImportReference(typeof(NullSafetyPrepatch).GetMethod(guard.Filter));
            PreloaderHelpers.WrapInExceptionFilter(method, filter);
            method.Body.Instructions.RecordPatchedCode(method);
            Console.WriteLine(
                $"[LinuxCompat] NullSafetyPrepatch: guarded {label} ({guard.Filter})"
            );
        }
    }

    // The filters take no game types, see WrapInExceptionFilter.

    public static bool NullReference(Exception e) => e is NullReferenceException;

    public static bool KeyNotFound(Exception e) => e is KeyNotFoundException;

    public static bool MissingSubpartOrNull(Exception e) =>
        e is KeyNotFoundException or NullReferenceException;

    public static bool TurretModelChange(Exception e, object instance)
    {
        if (e is not KeyNotFoundException)
            return false;
        var turret = (MyLargeTurretBase)instance;
        turret.m_base1 = null;
        turret.m_base2 = null;
        return true;
    }

    public static bool LaserAntennaModelChange(Exception e, object instance)
    {
        if (e is not KeyNotFoundException)
            return false;
        var antenna = (MyLaserAntenna)instance;
        antenna.m_base1 = null;
        antenna.m_base2 = null;
        return true;
    }
}
