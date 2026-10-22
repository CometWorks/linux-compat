#!/usr/bin/env python3
"""Stage the diagnostics mod as a Pulsar client mod for a client run.

Usage: prepare_client_mod.py <mod-source-dir> <root> <pulsar-legacy-dir> [<bin64>]

Pulsar loads a client mod (a Workshop id in its profile's Mods) from
<bin64>/../../../workshop/content/244850/<id>, compiles its scripts with a mod
context of its own and never adds it to the session's mods. That is the context
the suite covers in this mode. To keep the real Steam library untouched, this
script builds a stand-in library under <root>:

  <root>/steamapps/common/SpaceEngineers/*   symlinks to the game install
                                             (<bin64>/.., default the Steam one)
  <root>/steamapps/workshop/content/244850/<FAKE_ID>
                                             a copy of the mod

and registers <FAKE_ID> as a mod source in <pulsar-legacy-dir>/Sources/sources.xml
and in the Mods of its current profile. Start the client with the -bin64 path it
prints and load a world without the diagnostics mod, such as a copy of the
diagnostics world with an empty <Mods />. Use a Pulsar folder of your own: the
profile change enables the mod in every world that Pulsar loads.
"""

import shutil
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

FAKE_ID = "900000001"
GAME_APP_ID = "244850"


def link_install(bin64: Path, root: Path) -> Path:
    game = root / "steamapps/common/SpaceEngineers"
    game.mkdir(parents=True, exist_ok=True)
    for entry in bin64.parent.iterdir():
        link = game / entry.name
        if link.is_symlink():
            link.unlink()
        link.symlink_to(entry)
    return game / bin64.name


def copy_mod(src: Path, root: Path) -> None:
    dst = root / "steamapps/workshop/content" / GAME_APP_ID / FAKE_ID
    shutil.rmtree(dst, ignore_errors=True)
    shutil.copytree(src, dst, ignore=shutil.ignore_patterns("*.7z"))
    # Same as run.sh: a pair differing only in case can't be committed.
    (dst / "TestData/CaseSensitivity/casepair.txt").write_text("lower-case-file\n")


def register(legacy: Path) -> None:
    sources_path = legacy / "Sources/sources.xml"
    sources = ET.parse(sources_path)
    mods = sources.getroot().find("ModSources")
    if mods is None:
        mods = ET.SubElement(sources.getroot(), "ModSources")
    for mod in mods.findall("Mod"):
        if mod.findtext("ID") == FAKE_ID:
            mods.remove(mod)
    mod = ET.SubElement(mods, "Mod")
    for tag, value in (
        ("Name", "LinuxCompatDiagnostics"),
        ("ID", FAKE_ID),
        ("Enabled", "true"),
    ):
        ET.SubElement(mod, tag).text = value
    sources.write(sources_path, encoding="utf-8", xml_declaration=True)

    profile_path = legacy / "Profiles/Current.xml"
    profile = ET.parse(profile_path)
    ids = profile.getroot().find("Mods")
    if ids is None:
        ids = ET.SubElement(profile.getroot(), "Mods")
    if FAKE_ID not in (i.text for i in ids):
        ET.SubElement(ids, "unsignedLong").text = FAKE_ID
    profile.write(profile_path, encoding="utf-8", xml_declaration=True)


def main() -> None:
    if len(sys.argv) not in (4, 5):
        raise SystemExit(__doc__)
    src, root, legacy = (Path(a).expanduser().absolute() for a in sys.argv[1:4])
    default_bin64 = "~/.steam/steam/steamapps/common/SpaceEngineers/Bin64"
    bin64 = (
        Path(sys.argv[4] if len(sys.argv) == 5 else default_bin64)
        .expanduser()
        .resolve()
    )
    staged_bin64 = link_install(bin64, root)
    copy_mod(src, root)
    register(legacy)
    print(staged_bin64)


if __name__ == "__main__":
    main()
