#!/usr/bin/env python3
"""Generate the "Border Cleanup Test" world from a SmokeWorld save.

Produces a 1 km bounded (WorldSizeKm=1, boundaries +-500 m), creative,
mod-free empty world containing the template's character near the origin and
one single-block dynamic large grid ("BorderDriftShip") at x=400 m drifting
outward at 80 m/s. The grid crosses the broad-phase border (world boundary
inflated by 200 m by MyClusterTree) within a few seconds of simulation, which
must produce the SE-side cleanup log line
"HavokWorld_EntityLeftWorld removed entity" instead of a silent Havok-only
broad-phase removal.

Usage: make_world.py <SmokeWorld-save-dir> <output-world-dir>

Regenerates the committed world/ directory; run only when the template or the
scenario needs to change.
"""

import re
import sys
from pathlib import Path

DRIFT_GRID = """\
    <MyObjectBuilder_EntityBase xsi:type="MyObjectBuilder_CubeGrid">
      <SubtypeName />
      <EntityId>777000000000000001</EntityId>
      <PersistentFlags>CastShadows InScene</PersistentFlags>
      <Name>BorderDriftShip</Name>
      <PositionAndOrientation>
        <Position x="400" y="20" z="0" />
        <Forward x="0" y="0" z="-1" />
        <Up x="0" y="1" z="0" />
      </PositionAndOrientation>
      <LocalPositionAndOrientation xsi:nil="true" />
      <GridSizeEnum>Large</GridSizeEnum>
      <CubeBlocks>
        <MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_CubeBlock">
          <SubtypeName>LargeBlockArmorBlock</SubtypeName>
        </MyObjectBuilder_CubeBlock>
      </CubeBlocks>
      <LinearVelocity x="80" y="0" z="0" />
      <AngularVelocity x="0" y="0" z="0" />
      <DisplayName>BorderDriftShip</DisplayName>
      <DestructibleBlocks>true</DestructibleBlocks>
      <IsRespawnGrid>false</IsRespawnGrid>
      <LocalCoordSys>0</LocalCoordSys>
      <TargetingTargets />
      <NPCGridClaimElapsed xsi:nil="true" />
    </MyObjectBuilder_EntityBase>
"""


def sub1(pattern: str, repl: str, text: str, name: str) -> str:
    new, n = re.subn(pattern, repl, text, flags=re.DOTALL)
    if n < 1:
        raise SystemExit(f"pattern not found: {name}")
    return new


def fix_settings(text: str) -> str:
    text = sub1(r"<SessionName>[^<]*</SessionName>",
                "<SessionName>Border Cleanup Test</SessionName>", text, "SessionName")
    text = sub1(r"<WorldSizeKm>\d+</WorldSizeKm>",
                "<WorldSizeKm>1</WorldSizeKm>", text, "WorldSizeKm")
    text = sub1(r"<CargoShipsEnabled>true</CargoShipsEnabled>",
                "<CargoShipsEnabled>false</CargoShipsEnabled>", text, "CargoShipsEnabled")
    text = sub1(r"<Mods>.*?</Mods>", "<Mods />", text, "Mods")
    # The trash cleaner would delete the 1-block drift grid once it is 500 m
    # from the player, racing the border crossing this test is about.
    text = sub1(r"<TrashRemovalEnabled>true</TrashRemovalEnabled>",
                "<TrashRemovalEnabled>false</TrashRemovalEnabled>", text, "TrashRemovalEnabled")
    return text


def main() -> None:
    src, dst = Path(sys.argv[1]), Path(sys.argv[2])
    dst.mkdir(parents=True, exist_ok=True)

    checkpoint = fix_settings((src / "Sandbox.sbc").read_text(encoding="utf-8-sig"))
    checkpoint = sub1(
        r"<WorldBoundaries>.*?</WorldBoundaries>",
        '<WorldBoundaries>\n'
        '    <Min x="-500" y="-500" z="-500" />\n'
        '    <Max x="500" y="500" z="500" />\n'
        "  </WorldBoundaries>", checkpoint, "WorldBoundaries")
    (dst / "Sandbox.sbc").write_text(checkpoint, encoding="utf-8")

    config = fix_settings((src / "Sandbox_config.sbc").read_text(encoding="utf-8-sig"))
    (dst / "Sandbox_config.sbc").write_text(config, encoding="utf-8")

    sector = (src / "SANDBOX_0_0_0_.sbs").read_text(encoding="utf-8-sig")
    # No cargo ship spawns in the bounded test world.
    sector = sub1(r"\s*<MyObjectBuilder_GlobalEventBase>.*?</MyObjectBuilder_GlobalEventBase>",
                  "", sector, "SpawnCargoShip event")
    # Keep only the character; replace every grid with the drift ship.
    m = re.search(r'( *<MyObjectBuilder_EntityBase xsi:type="MyObjectBuilder_Character">.*?'
                  r"</MyObjectBuilder_EntityBase>\n)", sector, re.DOTALL)
    if not m:
        raise SystemExit("character entity not found")
    head = sector[: sector.index("<SectorObjects>") + len("<SectorObjects>")]
    (dst / "SANDBOX_0_0_0_.sbs").write_text(
        head + "\n" + m.group(1) + DRIFT_GRID + "  </SectorObjects>\n</MyObjectBuilder_Sector>",
        encoding="utf-8")

    # The game loads the binary sector (.sbsB5) in preference to the XML, and
    # the Remote API load path refuses XML-only saves outright, so a stale B5
    # would silently shadow the sector written above. Drop it; regenerate by
    # loading the world once through the in-game Load Game UI (which converts
    # the XML and writes the B5) and copying the B5 back here.
    b5 = dst / "SANDBOX_0_0_0_.sbsB5"
    if b5.exists():
        b5.unlink()
        print("deleted stale SANDBOX_0_0_0_.sbsB5 - regenerate it in-game "
              "(one UI load) and copy it back, or run.sh cannot load the world")

    print(f"wrote world to {dst}")


if __name__ == "__main__":
    main()
