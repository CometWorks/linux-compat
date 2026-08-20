using System;
using System.IO;
using System.Runtime.InteropServices;
using VRage.FileSystem;

namespace ClientPlugin.Compatibility;

/// <summary>
/// Applies the largest supported 32-bit DIB from a Windows ICO file.
/// </summary>
internal static class SdlIconHelper
{
    private const uint SdlPixelFormatBgra32 = 0x16862004u;

    /// <summary>
    /// Applies an icon relative to Bin64. Invalid or unsupported files are ignored.
    /// </summary>
    internal static void Apply(IntPtr windowHandle, string gameIcon)
    {
        if (windowHandle == IntPtr.Zero || string.IsNullOrEmpty(gameIcon))
            return;

        string path = Path.Combine(MyFileSystem.ExePath, gameIcon);
        if (!File.Exists(path))
            return;

        try
        {
            if (!TryLoadIcoSurface(path, out byte[] pixelData, out int width, out int height, out int pitch))
                return;

            GCHandle handle = GCHandle.Alloc(pixelData, GCHandleType.Pinned);
            try
            {
                IntPtr surface = SDL_CreateSurfaceFrom(width, height, SdlPixelFormatBgra32, handle.AddrOfPinnedObject(), pitch);
                if (surface == IntPtr.Zero)
                    return;

                try
                {
                    SDL_SetWindowIcon(windowHandle, surface);
                }
                finally
                {
                    SDL_DestroySurface(surface);
                }
            }
            finally
            {
                handle.Free();
            }
        }
        catch (Exception)
        {
        }
    }

    private static bool TryLoadIcoSurface(string path, out byte[] pixelData, out int width, out int height, out int pitch)
    {
        pixelData = null;
        width = 0;
        height = 0;
        pitch = 0;
        using FileStream stream = File.OpenRead(path);
        using BinaryReader reader = new BinaryReader(stream);
        if (reader.ReadUInt16() != 0 || reader.ReadUInt16() != 1)
            return false;

        int imageCount = reader.ReadUInt16();
        if (imageCount <= 0)
            return false;

        IcoEntry[] entries = new IcoEntry[imageCount];
        for (int i = 0; i < imageCount; i++)
        {
            entries[i] = new IcoEntry
            {
                Width = NormalizeIcoDimension(reader.ReadByte()),
                Height = NormalizeIcoDimension(reader.ReadByte()),
                ColorCount = reader.ReadByte(),
                Reserved = reader.ReadByte(),
                Planes = reader.ReadUInt16(),
                BitCount = reader.ReadUInt16(),
                BytesInRes = reader.ReadUInt32(),
                ImageOffset = reader.ReadUInt32()
            };
        }

        Array.Sort(entries, CompareIcoEntries);
        for (int i = 0; i < entries.Length; i++)
        {
            if (TryDecodeIcoEntry(stream, reader, entries[i], out pixelData, out width, out height, out pitch))
                return true;
        }

        pixelData = null;
        return false;
    }

    private static bool TryDecodeIcoEntry(FileStream stream, BinaryReader reader, IcoEntry entry, out byte[] pixelData, out int width, out int height, out int pitch)
    {
        pixelData = null;
        width = 0;
        height = 0;
        pitch = 0;
        if (entry.ImageOffset == 0 || entry.BytesInRes < 40)
            return false;

        stream.Position = entry.ImageOffset;
        uint headerSize = reader.ReadUInt32();
        if (headerSize < 40)
            return false;

        int dibWidth = reader.ReadInt32();
        int dibHeight = reader.ReadInt32();
        _ = reader.ReadUInt16();
        ushort bitsPerPixel = reader.ReadUInt16();
        uint compression = reader.ReadUInt32();
        if (compression != 0 || bitsPerPixel != 32 || dibWidth <= 0 || dibHeight <= 0)
            return false;

        _ = reader.ReadUInt32();
        _ = reader.ReadInt32();
        _ = reader.ReadInt32();
        _ = reader.ReadUInt32();
        _ = reader.ReadUInt32();
        width = dibWidth;
        height = dibHeight / 2;
        pitch = width * 4;
        pixelData = new byte[pitch * height];
        int xorStride = ((width * bitsPerPixel + 31) / 32) * 4;
        byte[] xorData = reader.ReadBytes(xorStride * height);
        int andStride = ((width + 31) / 32) * 4;
        byte[] andMask = reader.ReadBytes(andStride * height);
        if (xorData.Length < xorStride * height)
        {
            pixelData = null;
            return false;
        }

        for (int y = 0; y < height; y++)
        {
            int sourceRow = (height - 1 - y) * xorStride;
            int maskRow = (height - 1 - y) * andStride;
            int destinationRow = y * pitch;
            for (int x = 0; x < width; x++)
            {
                int sourceIndex = sourceRow + x * 4;
                int destinationIndex = destinationRow + x * 4;
                pixelData[destinationIndex] = xorData[sourceIndex];
                pixelData[destinationIndex + 1] = xorData[sourceIndex + 1];
                pixelData[destinationIndex + 2] = xorData[sourceIndex + 2];
                byte alpha = xorData[sourceIndex + 3];
                if (alpha == 0 && andMask.Length >= andStride * height)
                {
                    int maskByte = andMask[maskRow + x / 8];
                    bool transparent = ((maskByte >> (7 - x % 8)) & 1) != 0;
                    alpha = transparent ? (byte)0 : byte.MaxValue;
                }

                pixelData[destinationIndex + 3] = alpha;
            }
        }

        return true;
    }

    private static int CompareIcoEntries(IcoEntry left, IcoEntry right)
    {
        int leftScore = left.Width * left.Height * Math.Max((int)left.BitCount, 1);
        int rightScore = right.Width * right.Height * Math.Max((int)right.BitCount, 1);
        return rightScore.CompareTo(leftScore);
    }

    private static int NormalizeIcoDimension(byte dimension)
    {
        return (dimension == 0) ? 256 : dimension;
    }

    private struct IcoEntry
    {
        public int Width;
        public int Height;
        public byte ColorCount;
        public byte Reserved;
        public ushort Planes;
        public ushort BitCount;
        public uint BytesInRes;
        public uint ImageOffset;
    }

    private const string Lib = "libSDL3.so";

    [DllImport(Lib, EntryPoint = "SDL_CreateSurfaceFrom")]
    private static extern IntPtr SDL_CreateSurfaceFrom(int width, int height, uint format, IntPtr pixels, int pitch);

    [DllImport(Lib, EntryPoint = "SDL_SetWindowIcon")]
    private static extern bool SDL_SetWindowIcon(IntPtr window, IntPtr surface);

    [DllImport(Lib, EntryPoint = "SDL_DestroySurface")]
    private static extern void SDL_DestroySurface(IntPtr surface);
}
