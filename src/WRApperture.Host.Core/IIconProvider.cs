namespace WRApperture.Host.Core;

public interface IIconProvider
{
    /// <summary>Returns a PNG. Never throws: falls back to a generic application icon.</summary>
    byte[] GetPng(string iconPath, int iconIndex, int size = 48);
}
