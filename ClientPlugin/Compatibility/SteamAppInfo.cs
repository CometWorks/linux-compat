using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ClientPlugin.Compatibility;

// Reads the release state of apps from the Steam client's app info cache,
// appcache/appinfo.vdf. Steamworks gives a game no way to ask whether a DLC is
// released, but the Steam client keeps the store's common/ReleaseState
// ("released", "prerelease", ...) of every app it knows in this file, the
// game's DLC included. The file is binary VDF in format 28 (Steam since 2022)
// or 29 (since 2024). Anything unexpected throws, and the caller then treats
// the release state as unknown.
static class SteamAppInfo
{
    const uint Format28 = 0x07564428;
    const uint Format29 = 0x07564429;

    // Bytes between an entry's size and its key-values: info state, last
    // updated, PICS token, SHA-1, change number, SHA-1 of the binary data.
    const int EntryHeaderSize = 60;

    // Native Steam links its root from these; inside the Flatpak sandbox HOME
    // is the Flatpak's own folder, where the same links exist.
    static readonly string[] SteamRoots = [".steam/root", ".steam/steam", ".local/share/Steam"];

    internal static string FindCache()
    {
        var home = Environment.GetEnvironmentVariable("HOME");
        if (string.IsNullOrEmpty(home))
            return null;

        foreach (var root in SteamRoots)
        {
            var path = Path.Combine(home, root, "appcache", "appinfo.vdf");
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    // ReleaseState of each of the given apps that the cache has one for.
    internal static Dictionary<uint, string> ReadReleaseStates(
        string path,
        ICollection<uint> appIds
    )
    {
        var states = new Dictionary<uint, string>();
        using var reader = new BinaryReader(File.OpenRead(path));
        var stream = reader.BaseStream;

        var format = reader.ReadUInt32();
        if (format != Format28 && format != Format29)
            throw new InvalidDataException($"unknown appinfo.vdf format 0x{format:X8}");
        reader.ReadUInt32(); // universe

        // Format 29 stores each key once, in a table at the end of the file.
        string[] keys = null;
        var end = stream.Length;
        if (format == Format29)
        {
            end = reader.ReadInt64();
            var entries = stream.Position;
            stream.Position = end;
            keys = new string[reader.ReadUInt32()];
            for (var i = 0; i < keys.Length; i++)
                keys[i] = ReadString(reader);
            stream.Position = entries;
        }

        while (stream.Position < end)
        {
            var appId = reader.ReadUInt32();
            if (appId == 0)
                break;

            var size = reader.ReadUInt32();
            var next = stream.Position + size;
            if (appIds.Contains(appId))
            {
                stream.Position += EntryHeaderSize;
                var root = ReadNode(reader, keys);
                if (
                    Child(Child(root, "appinfo"), "common")
                        ?.TryGetValue("ReleaseState", out var state) == true
                    && state is string text
                )
                    states[appId] = text;
            }
            stream.Position = next;
        }
        return states;
    }

    static Dictionary<string, object> Child(Dictionary<string, object> node, string key) =>
        node != null && node.TryGetValue(key, out var child)
            ? child as Dictionary<string, object>
            : null;

    // Binary key-values: a type byte, the key, the value; type 8 ends a node.
    static Dictionary<string, object> ReadNode(BinaryReader reader, string[] keys)
    {
        var node = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var type = reader.ReadByte();
            if (type == 8)
                return node;

            var key = keys == null ? ReadString(reader) : keys[reader.ReadInt32()];
            node[key] = type switch
            {
                0 => ReadNode(reader, keys),
                1 => ReadString(reader),
                2 or 3 or 4 or 6 => reader.ReadInt32(), // int, float, pointer, color
                7 or 10 => reader.ReadInt64(), // uint64, int64
                _ => throw new InvalidDataException($"unknown key-value type {type}"),
            };
        }
    }

    static string ReadString(BinaryReader reader)
    {
        var bytes = new List<byte>();
        for (byte b; (b = reader.ReadByte()) != 0; )
            bytes.Add(b);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}
