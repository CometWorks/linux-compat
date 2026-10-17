using System.Text;

namespace DlcOwnershipTests;

// Writes a minimal appinfo.vdf: per app appinfo/common/{name, ReleaseState}.
static class AppInfoFile
{
    public static void Write(string path, int format, params (uint Id, string State)[] apps)
    {
        var keys = new List<string>();
        using var file = new BinaryWriter(File.Create(path));
        file.Write(format == 29 ? 0x07564429u : 0x07564428u);
        file.Write(1u); // universe
        if (format == 29)
            file.Write(0L); // key table offset, patched below

        foreach (var (id, state) in apps)
        {
            var body = new MemoryStream();
            var w = new BinaryWriter(body);
            w.Write(new byte[60]); // entry header, not read
            Node(w, keys, format, "appinfo");
            Node(w, keys, format, "common");
            Text(w, keys, format, "name", $"App {id}");
            if (state != null)
                Text(w, keys, format, "ReleaseState", state);
            w.Write((byte)8);
            w.Write((byte)8);
            w.Write((byte)8);
            file.Write(id);
            file.Write((uint)body.Length);
            file.Write(body.ToArray());
        }
        file.Write(0u);

        if (format != 29)
            return;
        var table = file.BaseStream.Position;
        file.Write((uint)keys.Count);
        foreach (var key in keys)
            Zero(file, key);
        file.BaseStream.Position = 8;
        file.Write(table);
    }

    static void Node(BinaryWriter w, List<string> keys, int format, string key)
    {
        w.Write((byte)0);
        Key(w, keys, format, key);
    }

    static void Text(BinaryWriter w, List<string> keys, int format, string key, string value)
    {
        w.Write((byte)1);
        Key(w, keys, format, key);
        Zero(w, value);
    }

    static void Key(BinaryWriter w, List<string> keys, int format, string key)
    {
        if (format != 29)
        {
            Zero(w, key);
            return;
        }
        if (!keys.Contains(key))
            keys.Add(key);
        w.Write(keys.IndexOf(key));
    }

    static void Zero(BinaryWriter w, string text)
    {
        w.Write(Encoding.UTF8.GetBytes(text));
        w.Write((byte)0);
    }
}
