#!/usr/bin/env python3
"""Prepare the local Magnetar dedicated server for a diagnostics run.

Usage: prepare_ds.py <mod-source-dir>

Idempotent. Performs the offline fake-Workshop registration procedure:
  1. Copies the mod to <instance>/content/244850/<FAKE_ID>/.
  2. Registers <FAKE_ID> in appworkshop_244850.acf (WorkshopItemsInstalled +
     WorkshopItemDetails, manifest == latest_manifest so NeedsDownload stays 0).
  3. Creates/refreshes the scratch world "LinuxCompat Mod API Suite" from
     "Empty Creative" with the diagnostics mod as its only mod.
  4. Points <LoadWorld> at the scratch world (SpaceEngineers-Dedicated.cfg is
     UTF-8 without BOM; the edit preserves the rest byte-for-byte).

The DS rejects local mods in multiplayer, hence the fake Workshop identity.
Run only while the DS is stopped (it re-saves the world on shutdown).
"""

import os
import re
import shutil
import subprocess
import sys

FAKE_ID = "900000001"
INSTANCE = os.path.expanduser("~/.config/SpaceEngineersDedicated")
ACF = os.path.join(INSTANCE, "appworkshop_244850.acf")
CONTENT_DIR = os.path.join(INSTANCE, "content", "244850", FAKE_ID)
TEMPLATE_WORLD = os.path.join(INSTANCE, "Saves", "Empty Creative")
WORLD = os.path.join(INSTANCE, "Saves", "LinuxCompat Mod API Suite")
WORLD_NAME = "LinuxCompat Mod API Suite"
CFG = os.path.join(INSTANCE, "SpaceEngineers-Dedicated.cfg")

MOD_ITEM_CONFIG = (
    "<ModItem FriendlyName=\"LinuxCompatDiagnostics\">"
    f"<Name>{FAKE_ID}.sbm</Name>"
    f"<PublishedFileId>{FAKE_ID}</PublishedFileId>"
    "<PublishedServiceName>Steam</PublishedServiceName>"
    "<IsDependency>false</IsDependency>"
    "</ModItem>"
)


def deploy_mod(source: str) -> None:
    os.makedirs(os.path.dirname(CONTENT_DIR), exist_ok=True)
    subprocess.run(
        ["rsync", "-a", "--delete", "--exclude", "*.7z", source.rstrip("/") + "/", CONTENT_DIR + "/"],
        check=True,
    )
    # Case-pair negative control: the lowercase sibling is generated at
    # deploy time (cannot live in git next to CasePair.txt).
    with open(os.path.join(CONTENT_DIR, "TestData", "CaseSensitivity", "casepair.txt"), "w") as f:
        f.write("lower-case-file\n")
    print(f"mod deployed to {CONTENT_DIR}")


def dir_size(path: str) -> int:
    total = 0
    for root, _dirs, files in os.walk(path):
        for name in files:
            total += os.path.getsize(os.path.join(root, name))
    return total


def register_acf() -> None:
    with open(ACF, encoding="utf-8") as f:
        text = f.read()

    if f'"{FAKE_ID}"' in text:
        print("ACF already registers the fake id")
        return

    size = dir_size(CONTENT_DIR)
    installed_entry = (
        f'\t\t"{FAKE_ID}"\n\t\t{{\n'
        f'\t\t\t"size"\t\t"{size}"\n'
        f'\t\t\t"timeupdated"\t\t"1756500000"\n'
        f'\t\t\t"manifest"\t\t"1"\n'
        f"\t\t}}\n"
    )
    details_entry = (
        f'\t\t"{FAKE_ID}"\n\t\t{{\n'
        f'\t\t\t"manifest"\t\t"1"\n'
        f'\t\t\t"timeupdated"\t\t"1756500000"\n'
        f'\t\t\t"timetouched"\t\t"1756500000"\n'
        f'\t\t\t"latest_timeupdated"\t\t"1756500000"\n'
        f'\t\t\t"latest_manifest"\t\t"1"\n'
        f"\t\t}}\n"
    )

    def insert_into(section: str, entry: str, hay: str) -> str:
        # Insert right after the section's opening brace line.
        marker = f'"{section}"\n\t{{\n'
        idx = hay.index(marker) + len(marker)
        return hay[:idx] + entry + hay[idx:]

    shutil.copy2(ACF, ACF + ".bak-modapi")
    text = insert_into("WorkshopItemsInstalled", installed_entry, text)
    text = insert_into("WorkshopItemDetails", details_entry, text)
    with open(ACF, "w", encoding="utf-8", newline="") as f:
        f.write(text)
    print(f"ACF registered fake id {FAKE_ID} (size={size})")


def make_world() -> None:
    if os.path.isdir(WORLD):
        shutil.rmtree(WORLD)
    shutil.copytree(TEMPLATE_WORLD, WORLD)
    for name in ("Sandbox_config.sbc", "Sandbox.sbc"):
        path = os.path.join(WORLD, name)
        with open(path, encoding="utf-8") as f:
            text = f.read()
        text = text.replace(
            "<SessionName>Empty Creative</SessionName>",
            f"<SessionName>{WORLD_NAME}</SessionName>",
        )
        mods = f"<Mods>{MOD_ITEM_CONFIG}</Mods>"
        if re.search(r"<Mods\s*/>", text):
            text = re.sub(r"<Mods\s*/>", mods, text, count=1)
        elif "<Mods>" in text:
            text = re.sub(r"<Mods>.*?</Mods>", mods, text, count=1, flags=re.S)
        else:
            raise SystemExit(f"no <Mods> element found in {path}")
        with open(path, "w", encoding="utf-8", newline="") as f:
            f.write(text)
    # Drop stale backups from the template copy.
    backup = os.path.join(WORLD, "Backup")
    if os.path.isdir(backup):
        shutil.rmtree(backup)
    print(f"scratch world ready: {WORLD}")


def point_last_session() -> None:
    # The DS boots via "Loading last session" and ignores <LoadWorld>, so the
    # scratch world must also be recorded as the last session.
    path = os.path.join(INSTANCE, "Saves", "LastSession.sbl")
    if os.path.exists(path) and not os.path.exists(path + ".bak-modapi"):
        shutil.copy2(path, path + ".bak-modapi")
    with open(path, "w", encoding="utf-8", newline="") as f:
        f.write(
            '<?xml version="1.0" encoding="utf-8"?>\n'
            "<MyObjectBuilder_LastSession "
            'xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" '
            'xmlns:xsd="http://www.w3.org/2001/XMLSchema">\n'
            f"  <Path>{WORLD}</Path>\n"
            "  <IsContentWorlds>false</IsContentWorlds>\n"
            "  <IsOnline>false</IsOnline>\n"
            "  <IsLobby>false</IsLobby>\n"
            f"  <GameName>{WORLD_NAME}</GameName>\n"
            "  <ServerPort>0</ServerPort>\n"
            f"  <RelativePath>{os.path.basename(WORLD)}</RelativePath>\n"
            "</MyObjectBuilder_LastSession>"
        )
    print(f"LastSession.sbl -> {WORLD}")


def point_cfg() -> None:
    with open(CFG, "rb") as f:
        raw = f.read()
    text = raw.decode("utf-8")
    new = re.sub(
        r"<LoadWorld>[^<]*</LoadWorld>",
        f"<LoadWorld>{WORLD}</LoadWorld>",
        text,
        count=1,
    )
    if new == text:
        print("cfg already points at the scratch world")
        return
    with open(CFG + ".bak-modapi", "wb") as f:
        f.write(raw)
    with open(CFG, "w", encoding="utf-8", newline="") as f:
        f.write(new)
    print(f"cfg LoadWorld -> {WORLD}")


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__, file=sys.stderr)
        return 2
    deploy_mod(sys.argv[1])
    register_acf()
    make_world()
    point_last_session()
    point_cfg()
    return 0


if __name__ == "__main__":
    sys.exit(main())
