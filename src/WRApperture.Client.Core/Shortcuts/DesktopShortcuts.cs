using System.Buffers.Binary;

namespace WRApperture.Client.Core.Shortcuts;

/// <summary>Wraps a PNG in an ICO container (PNG-in-ICO is supported since Windows Vista).</summary>
public static class IcoWriter
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static byte[] FromPng(byte[] png) => FromPngs([png]);

    /// <summary>One entry per image (e.g. 16, 32, 48 and 256 px), so Windows picks a sharp one at each size.</summary>
    public static byte[] FromPngs(IReadOnlyList<byte[]> pngs)
    {
        if (pngs.Count == 0 || pngs.Any(png => png.Length < 24 || !png.AsSpan(0, 8).SequenceEqual(PngSignature)))
        {
            throw new ArgumentException("Not a PNG image.", nameof(pngs));
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);          // reserved
        writer.Write((ushort)1);          // type: icon
        writer.Write((ushort)pngs.Count); // image count

        var offset = 6 + 16 * pngs.Count;
        foreach (var png in pngs)
        {
            // IHDR width/height: big-endian at offsets 16 and 20. ICO stores 256 as 0.
            var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
            var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
            writer.Write((byte)(width >= 256 ? 0 : width));
            writer.Write((byte)(height >= 256 ? 0 : height));
            writer.Write((byte)0);        // palette
            writer.Write((byte)0);        // reserved
            writer.Write((ushort)1);      // planes
            writer.Write((ushort)32);     // bits per pixel
            writer.Write((uint)png.Length);
            writer.Write((uint)offset);   // data offset
            offset += png.Length;
        }

        foreach (var png in pngs)
        {
            writer.Write(png);
        }

        return stream.ToArray();
    }
}

public static class DesktopShortcuts
{
    public static string SafeName(string displayName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(displayName.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim().TrimEnd('.');
        return string.IsNullOrEmpty(cleaned) ? "WRApperture" : cleaned;
    }

    /// <summary>
    /// The user's "WRApperture" Start menu folder: apps there show up in Windows search and can be pinned.
    /// The uninstaller cleans the same folder.
    /// </summary>
    public static string StartMenuDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "WRApperture");

    /// <summary>Creates (or replaces) a .lnk that opens <paramref name="rdpFile"/>; on the user's desktop by default.</summary>
    public static string Create(string displayName, string rdpFile, string icoFile, string? directory = null)
    {
        directory ??= Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, SafeName(displayName) + ".lnk");

        var type = Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!;
        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(path);
            shortcut.TargetPath = rdpFile;
            shortcut.IconLocation = icoFile + ",0";
            shortcut.Description = $"{displayName} (WRApperture)";
            shortcut.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }

        return path;
    }
}
