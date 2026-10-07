using System.Drawing;
using Microsoft.Extensions.Logging.Abstractions;
using WRApperture.Host.Core.Icons;

namespace WRApperture.Host.Core.Tests.Icons;

/// <summary>Uses the real Windows icon APIs (read-only, no system changes).</summary>
[Trait("Category", "Integration")]
public class IconProviderTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private readonly IconProvider _provider = new(NullLogger<IconProvider>.Instance);

    [Theory]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(256)]
    public void Extracts_png_of_requested_size_from_notepad(int size)
    {
        var png = _provider.GetPng(@"%SystemRoot%\System32\notepad.exe", 0, size);

        AssertPng(png, size);
        Assert.True(HasVisiblePixels(png));
    }

    [Fact]
    public void Falls_back_to_generic_icon_for_missing_file()
    {
        AssertPng(_provider.GetPng(@"C:\nao\existe.exe", 0), 48);
    }

    [Fact]
    public void Falls_back_for_file_without_icons()
    {
        var png = _provider.GetPng(@"%SystemRoot%\System32\drivers\etc\hosts", 0);

        AssertPng(png, 48);
    }

    [Fact]
    public void Clamps_size_and_index()
    {
        AssertPng(_provider.GetPng(@"%SystemRoot%\System32\notepad.exe", -5, 4000), IconProvider.MaxSize);
        AssertPng(_provider.GetPng(@"%SystemRoot%\System32\notepad.exe", 999, 1), IconProvider.MinSize);
    }

    private static void AssertPng(byte[] png, int size)
    {
        Assert.True(png.AsSpan().StartsWith(PngSignature));
        using var image = Image.FromStream(new MemoryStream(png));
        Assert.Equal(size, image.Width);
        Assert.Equal(size, image.Height);
    }

    private static bool HasVisiblePixels(byte[] png)
    {
        using var bitmap = new Bitmap(new MemoryStream(png));
        for (var x = 0; x < bitmap.Width; x += 4)
        {
            for (var y = 0; y < bitmap.Height; y += 4)
            {
                if (bitmap.GetPixel(x, y).A > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
