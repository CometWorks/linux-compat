# Bounded-world border cleanup test

Covers `MyPhysicsCreateHkWorldPatch` (Shared/Patches/NullSafety/MyPhysicsPatch.cs).

Both `MyPhysics.CreateHkWorld` paths configure `BROADPHASE_BORDER_REMOVE_ENTITY`,
so in a bounded world (`WorldSizeKm > 0`) Havok removes any body that crosses
the broad-phase border. Vanilla hooks `HavokWorld_EntityLeftWorld` so SE closes
the entity and logs `HavokWorld_EntityLeftWorld removed entity ...`; without
that handler the removal is Havok-only and SE keeps driving a stale broad-phase
handle. The patch's settings-less replacement path historically skipped the
hookup; it now defers the WorldSizeKm decision to event-fire time.

## What it does

`run.sh` deploys the committed 1 km world (`world/`, regenerate with
`make_world.py`), whose single-block dynamic grid **BorderDriftShip** starts at
x=400 m drifting outward at 80 m/s and crosses the border (world boundary
±500 m, inflated 200 m by MyClusterTree) a few seconds into the session. Both
phases assert the SE-side cleanup line appears in the game log:

- **Phase A** — vanilla creation path (settings present, prefix falls through).
  Also asserts the settings-less replacement path did NOT fire.
- **Phase B** — `SE_LINUX_COMPAT_FORCE_HKWORLD_PREFIX=1` forces every HkWorld
  through the patch's replacement path; the cleanup must arrive via the
  deferred `EntityLeftWorld` handler. Asserts the replacement path DID fire.

```bash
tests/physics-border/run.sh            # build + phase A + phase B
tests/physics-border/run.sh --skip-build --phase B
```

Exit 0 = pass. Same environment requirements as `tests/mod-api/run.sh`
(Pulsar Interim + Legacy dev profile, se-remote skill, machine-wide game lock
`~/.cache/se-game.lock`, always headless).

## Notes

- The committed `SANDBOX_0_0_0_.sbsB5` is required: the Remote API load path
  refuses XML-only saves (`MyLoadingNeedXMLException`), and a present B5
  shadows the XML sector. `make_world.py` deletes the B5 when it regenerates
  the sector; produce a fresh one by loading the world once through the
  in-game Load Game UI (which converts the XML and writes the B5) and copying
  it back into `world/`.

- The world disables cargo ships and trash removal: the trash cleaner would
  otherwise delete the 1-block grid once it is 500 m from the player, racing
  the border crossing under test.
- An entity placed entirely OUTSIDE the bounds at load gets no physics at all
  (`MyClusterTree.AddObject` returns `ulong.MaxValue` for a single-cluster
  tree), so only the drift-across scenario exercises the callback.
- The driver exits the game gracefully so the session-unload ordering lands in
  the same log; `run.sh` counts `[LinuxCompat] CreateHkWorld replacement path`
  lines to keep an empirical record of when the settings-less path fires
  (as of 2026-08-31: never, on boot, menu, world load, or unload).
