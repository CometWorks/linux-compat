#!/usr/bin/env python3
"""Prepare an isolated Magnetar dedicated server for a diagnostics run.

Usage: prepare_ds.py <mod-source-dir> <root> <repo-dir> <server-port> <steam-port>

Everything is written under <root>; nothing outside it is changed:
  <root>/magnetar  Magnetar config folder (-config). Its sources.xml registers
                   <repo-dir> as the dev folder source of the linux-compat core
                   plugin, so Magnetar compiles the working tree instead of
                   loading the MagnetarHub build. DOTNET_COMPAT_DIR, when set,
                   does the same for dotnet-compat.
  <root>/data      DS data folder (-path): its own SpaceEngineers-Dedicated.cfg
                   on the given ports, the fake-Workshop registration of the mod,
                   and the scratch world "LinuxCompat Mod API Suite" with the
                   diagnostics mod as its only mod.

The dedicated config and the "Empty Creative" world are copied from
TEMPLATE_INSTANCE (default ~/.config/SpaceEngineersDedicated), which is only read.

The DS rejects local mods in multiplayer, hence the fake Workshop identity.
Run only while this root's DS is stopped (it re-saves the world on shutdown).
"""

import os
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

FAKE_ID = "900000001"
WORLD_NAME = "LinuxCompat Mod API Suite"
TEMPLATE_INSTANCE = os.environ.get(
    "TEMPLATE_INSTANCE", os.path.expanduser("~/.config/SpaceEngineersDedicated")
)

MOD_ITEM_CONFIG = (
    '<ModItem FriendlyName="LinuxCompatDiagnostics">'
    f"<Name>{FAKE_ID}.sbm</Name>"
    f"<PublishedFileId>{FAKE_ID}</PublishedFileId>"
    "<PublishedServiceName>Steam</PublishedServiceName>"
    "<IsDependency>false</IsDependency>"
    "</ModItem>"
)

SOURCES_XML = """<?xml version="1.0" encoding="utf-8"?>
<SourcesConfig xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <ShowWarning>false</ShowWarning>
  <MaxSourceAge>2</MaxSourceAge>
  <LocalHubSources />
  <RemoteHubSources>
    <RemoteHub>
      <Name>MagnetarHub</Name>
      <Repo>CometWorks/magnetar-hub</Repo>
      <Branch>main</Branch>
      <Enabled>true</Enabled>
      <Trusted>true</Trusted>
    </RemoteHub>
  </RemoteHubSources>
  <RemotePluginSources />
  <LocalPluginSources />
  <ModSources />
</SourcesConfig>
"""

# No optional plugins: only the core plugins (dotnet-compat, linux-compat) load.
PROFILE_XML = """<?xml version="1.0" encoding="utf-8"?>
<Profile xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <Name>Current</Name>
  <GitHub />
  <DevFolder />
  <Local />
  <Mods />
</Profile>
"""

# manifest == latest_manifest keeps NeedsDownload at 0.
ACF = """"AppWorkshop"
{
\t"appid"\t\t"244850"
\t"NeedsUpdate"\t\t"0"
\t"NeedsDownload"\t\t"0"
\t"WorkshopItemsInstalled"
\t{
\t\t"%(id)s"
\t\t{
\t\t\t"size"\t\t"%(size)d"
\t\t\t"timeupdated"\t\t"1756500000"
\t\t\t"manifest"\t\t"1"
\t\t}
\t}
\t"WorkshopItemDetails"
\t{
\t\t"%(id)s"
\t\t{
\t\t\t"manifest"\t\t"1"
\t\t\t"timeupdated"\t\t"1756500000"
\t\t\t"timetouched"\t\t"1756500000"
\t\t\t"latest_timeupdated"\t\t"1756500000"
\t\t\t"latest_manifest"\t\t"1"
\t\t}
\t}
}
"""


def set_source(sources: str, name: str, folder: str | None) -> None:
    """Points the dev folder source of this name at a folder, or removes it when
    folder is None. The name must equal the core plugin id to override it."""
    tree = ET.parse(sources)
    local = tree.getroot().find("LocalPluginSources")
    for plugin in local.findall("LocalPlugin"):
        if plugin.findtext("Name") == name:
            local.remove(plugin)
    if folder:
        plugin = ET.SubElement(local, "LocalPlugin")
        for tag, value in (
            ("Name", name),
            ("Folder", folder),
            ("File", "ServerPlugin/ServerPlugin.xml"),
            ("Enabled", "true"),
        ):
            ET.SubElement(plugin, tag).text = value
        print(f"dev folder source {name} -> {folder}")
    tree.write(sources, encoding="utf-8", xml_declaration=True)


def prepare_magnetar(config: str, repo: str) -> None:
    # Kept between runs, so Magnetar's hub check state and plugin caches survive.
    sources = os.path.join(config, "Sources", "sources.xml")
    if not os.path.exists(sources):
        os.makedirs(os.path.dirname(sources), exist_ok=True)
        with open(sources, "w", encoding="utf-8") as f:
            f.write(SOURCES_XML)
    set_source(sources, "linux-compat", repo)
    dotnet_compat = os.environ.get("DOTNET_COMPAT_DIR")
    set_source(
        sources, "dotnet-compat", dotnet_compat and os.path.abspath(dotnet_compat)
    )

    os.makedirs(os.path.join(config, "Profiles"), exist_ok=True)
    with open(
        os.path.join(config, "Profiles", "Current.xml"), "w", encoding="utf-8"
    ) as f:
        f.write(PROFILE_XML)


def deploy_mod(source: str, data: str) -> str:
    content = os.path.join(data, "content", "244850", FAKE_ID)
    os.makedirs(os.path.dirname(content), exist_ok=True)
    subprocess.run(
        [
            "rsync",
            "-a",
            "--delete",
            "--exclude",
            "*.7z",
            source.rstrip("/") + "/",
            content + "/",
        ],
        check=True,
    )
    # Case-pair negative control: the lowercase sibling is generated at
    # deploy time (cannot live in git next to CasePair.txt).
    with open(
        os.path.join(content, "TestData", "CaseSensitivity", "casepair.txt"), "w"
    ) as f:
        f.write("lower-case-file\n")
    print(f"mod deployed to {content}")
    return content


def dir_size(path: str) -> int:
    total = 0
    for root, _dirs, files in os.walk(path):
        for name in files:
            total += os.path.getsize(os.path.join(root, name))
    return total


def make_world(world: str) -> None:
    if os.path.isdir(world):
        shutil.rmtree(world)
    shutil.copytree(
        os.path.join(TEMPLATE_INSTANCE, "Saves", "Empty Creative"),
        world,
        ignore=shutil.ignore_patterns("Backup"),
    )
    for name in ("Sandbox_config.sbc", "Sandbox.sbc"):
        path = os.path.join(world, name)
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
    print(f"scratch world ready: {world}")


def write_cfg(data: str, world: str, server_port: str, steam_port: str) -> None:
    with open(
        os.path.join(TEMPLATE_INSTANCE, "SpaceEngineers-Dedicated.cfg"),
        encoding="utf-8",
    ) as f:
        text = f.read()
    for tag, value in {
        "IP": "127.0.0.1",
        "ServerPort": server_port,
        "SteamPort": steam_port,
        "WorldName": WORLD_NAME,
        "PauseGameWhenEmpty": "false",
        "AutoRestartEnabled": "false",
        # Load <LoadWorld>, not Saves/LastSession.sbl
        "IgnoreLastSession": "true",
        "RemoteApiEnabled": "false",
        "LoadWorld": world,
    }.items():
        text, count = re.subn(
            rf"<{tag}>.*?</{tag}>|<{tag}\s*/>",
            f"<{tag}>{value}</{tag}>",
            text,
            flags=re.S,
        )
        if count != 1:
            raise SystemExit(f"<{tag}> not found in the template dedicated config")
    # UTF-8 without BOM, as the DS writes it
    with open(
        os.path.join(data, "SpaceEngineers-Dedicated.cfg"),
        "w",
        encoding="utf-8",
        newline="",
    ) as f:
        f.write(text)
    print(f"dedicated config: ports {server_port}/{steam_port}, world {world}")


def main() -> int:
    if len(sys.argv) != 6:
        print(__doc__, file=sys.stderr)
        return 2
    mod, root, repo, server_port, steam_port = sys.argv[1:]
    data = os.path.join(root, "data")
    world = os.path.join(data, "Saves", WORLD_NAME)
    prepare_magnetar(os.path.join(root, "magnetar"), os.path.abspath(repo))
    os.makedirs(data, exist_ok=True)
    content = deploy_mod(mod, data)
    with open(
        os.path.join(data, "appworkshop_244850.acf"), "w", encoding="utf-8", newline=""
    ) as f:
        f.write(ACF % {"id": FAKE_ID, "size": dir_size(content)})
    make_world(world)
    write_cfg(data, world, server_port, steam_port)
    return 0


if __name__ == "__main__":
    sys.exit(main())
