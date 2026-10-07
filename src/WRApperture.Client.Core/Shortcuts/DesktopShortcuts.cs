using System.Buffers.Binary;

namespace WRApperture.Client.Core.Shortcuts;

/// <summary>Wraps a PNG in an ICO container (PNG-in-ICO is supported since Windows Vista).</summary>
public static class IcoWriter
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static byte[] FromPng(byte[] png)
    {
        if (png.Length < 24 || !png.AsSpan(0, 8).SequenceEqual(PngSignature))
        {
            throw new ArgumentException("Not a PNG image.", nameof(png));
        }

        // IHDR width/height: big-endian at offsets 16 and 20. ICO stores 256 as 0.
        var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));

        using var stream = new MemoryStream(22 + png.Length);
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);          // reserved
        writer.Write((ushort)1);          // type: icon
        writer.Write((ushort)1);          // image count
        writer.Write((byte)(width >= 256 ? 0 : width));
        writer.Write((byte)(height >= 256 ? 0 : height));
        writer.Write((byte)0);            // palette
        writer.Write((byte)0);            // reserved
        writer.Write((ushort)1);          // planes
        writer.Write((ushort)32);         // bits per pixel
        writer.Write((uint)png.Length);
        writer.Write((uint)22);           // data offset
        writer.Write(png);
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

    /// <summary>Creates (or replaces) a .lnk on the user's desktop that opens <paramref name="rdpFile"/>.</summary>
    public static string Create(string displayName, string rdpFile, string icoFile, string? directory = null)
    {
        directory ??= Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
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
